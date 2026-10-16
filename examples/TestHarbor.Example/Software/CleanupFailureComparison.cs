using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace TestHarbor.Example.Software;

/// <summary>Runs the same failed shared-table cleanup through actual NUnit setup/teardown with and without TestHarbor.</summary>
/// <param name="useHarbor">Whether to add TestHarbor ownership and quarantine to the NUnit lifecycle.</param>
/// <remarks>Ordering is intentional scenario orchestration, not advice to make ordinary tests order-dependent.</remarks>
[TestFixture(false)]
[TestFixture(true)]
[Explicit("Run through tools/verify_cleanup_comparison.py; four failures are intentional.")]
[NonParallelizable]
public sealed class CleanupFailureComparison(bool useHarbor)
{
    /// <summary>Models the records in one shared test table.</summary>
    private int rows;
    /// <summary>Injects a transient reset failure without removing existing rows.</summary>
    private bool resetFails;
    /// <summary>Provides the plain fixture's manual mutual-exclusion policy.</summary>
    private readonly SemaphoreSlim gate = new(1, 1);
    /// <summary>Records successful manual acquisition for safe teardown.</summary>
    private bool acquired;
    /// <summary>Provides isolated TestHarbor ownership state for the wrapper scenario.</summary>
    private readonly ResourceCoordinator coordinator = new();
    /// <summary>Retains the active session between NUnit setup and teardown.</summary>
    private TestSession? session;
    /// <summary>Identifies scenario evidence without depending on fixture display-name formatting.</summary>
    /// <value>Plain or harbor depending on the fixture parameter.</value>
    private string Mode => useHarbor ? "harbor" : "plain";

    /// <summary>Acquires ownership except for the explicit operator-recovery step.</summary>
    /// <returns>A task completing after ownership is granted, or failing if the resource is quarantined.</returns>
    [SetUp]
    public async Task Setup()
    {
        var method = TestContext.CurrentContext.Test.MethodName;
        TestContext.Out.WriteLine($"SCENARIO:{Mode}:{method}");
        if (method == nameof(Recovery)) return;
        // The reset service is available again, but the old rows have not magically disappeared.
        if (method == nameof(NextTestExpectsEmptyTable)) resetFails = false;
        if (useHarbor)
            session = await TestSession.OpenAsync([new("TestDatabase/Orders")],
                services => services.AddScoped<ITestExtension>(_ => new ResetExtension(this)), coordinator);
        else
        {
            await gate.WaitAsync();
            acquired = true;
        }
    }

    /// <summary>Writes one record successfully, then arranges for teardown's reset to fail.</summary>
    [Test]
    public void FirstTestWritesRecord()
    {
        Assert.That(rows, Is.Zero);
        rows = 1;
        resetFails = true;
        Record("write:1");
        Assert.That(rows, Is.EqualTo(1));
        TestContext.Out.WriteLine("The application assertion passed. The injected failure occurs later, in cleanup.");
    }

    /// <summary>Requires an empty shared table and detects contamination if allowed to execute.</summary>
    [Test, DependsOnTest(nameof(FirstTestWritesRecord), AllowFailure = true)]
    public void NextTestExpectsEmptyTable()
    {
        Record("read:" + rows);
        TestContext.Out.WriteLine("Plain NUnit reached the next body with stale data; TestHarbor must reject acquisition before this body.");
        Assert.That(rows, Is.Zero, "Previous cleanup failed: this is stale test data, not an application regression.");
    }

    /// <summary>Performs an explicit reset, then verifies ownership and clean data after recovery.</summary>
    /// <returns>A task completing after the recovered environment has been verified.</returns>
    [Test, DependsOnTest(nameof(NextTestExpectsEmptyTable), AllowFailure = true)]
    public async Task Recovery()
    {
        Record("recover");
        resetFails = false;
        Reset();
        Assert.That(rows, Is.Zero);
        if (useHarbor)
        {
            coordinator.ResetQuarantine("TestDatabase/Orders", "Test table successfully reset and verified empty.");
            await TestSession.RunAsync([new("TestDatabase/Orders")], (_, _) =>
            {
                Record("recovered-read:" + rows);
                Assert.That(rows, Is.Zero);
                return Task.CompletedTask;
            }, coordinator: coordinator);
        }
        else Record("recovered-read:" + rows);
        TestContext.Out.WriteLine("Recovery was performed and verified. TestHarbor's quarantine reset grants reuse; it does not clean the database itself.");
    }

    /// <summary>Attempts reset under ownership and releases ownership even when cleanup reports failure.</summary>
    /// <returns>A task completing after cleanup, or reporting the intentionally injected reset failure.</returns>
    [TearDown]
    public async Task Teardown()
    {
        var current = session;
        session = null;
        if (current is not null) await current.DisposeAsync();
        if (acquired)
        {
            try { Reset(); }
            finally { acquired = false; gate.Release(); }
        }
    }

    /// <summary>Disposes the manually allocated synchronization primitive after all scenario steps.</summary>
    [OneTimeTearDown]
    public void CloseGate() => gate.Dispose();

    /// <summary>Simulates database reset success or a transient failure before any records are removed.</summary>
    private void Reset()
    {
        if (resetFails)
        {
            Record("reset-failed");
            throw new IOException("Simulated database reset timeout; records remain.");
        }
        rows = 0;
        Record("reset-ok");
    }

    /// <summary>Writes independent scenario evidence so the external verifier can prove which bodies ran.</summary>
    /// <param name="entry">Event or observed record count.</param>
    private void Record(string entry)
    {
        var directory = Environment.GetEnvironmentVariable("TESTHARBOR_COMPARISON_OUTPUT")
            ?? throw new InvalidOperationException("Run tools/verify_cleanup_comparison.py.");
        File.AppendAllLines(Path.Combine(directory, Mode + ".txt"), [entry]);
        TestContext.Out.WriteLine($"[{Mode}] {entry}");
    }

    /// <summary>Adapts the same reset operation to TestHarbor's cleanup lifecycle.</summary>
    /// <param name="fixture">Scenario owning the shared table and failure injection.</param>
    private sealed class ResetExtension(CleanupFailureComparison fixture) : ITestExtension
    {
        /// <summary>Leaves existing state untouched so the comparison tests the cleanup policy alone.</summary>
        /// <param name="context">Session services and owner identity.</param>
        /// <param name="cancellationToken">Startup cancellation.</param>
        /// <returns>A completed startup task.</returns>
        public Task StartAsync(TestExtensionContext context, CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>Runs the same table reset as the manually managed NUnit fixture.</summary>
        /// <param name="context">Session services available during cleanup.</param>
        /// <returns>A completed task if reset succeeds.</returns>
        public Task StopAsync(TestExtensionContext context)
        {
            fixture.Reset();
            return Task.CompletedTask;
        }
    }
}
