using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor.NUnit;

namespace TestHarbor.Tests;

// Deliberate failures are opt-in and validated by tools/verify_failure_probes.py.
/// <summary>Exercises intentional NUnit setup, test, teardown and cleanup failures for the external verifier.</summary>
[Explicit("Run with the failure-probe verifier; all four cases intentionally fail.")]
public sealed class LifecycleFailureProbes : HarborFixture
{
    /// <summary>Gets the current parameterized failure stage from the public NUnit test context.</summary>
    /// <value>The failure stage passed as the current NUnit test argument.</value>
    private string Mode => (string)TestContext.CurrentContext.Test.Arguments[0]!;
    /// <summary>Declares the resource set retained throughout this fixture test and its cleanup.</summary>
    /// <value>The complete set of required resources for this test.</value>
    protected override IEnumerable<ResourceRequest> Resources => [new("FailureProbe/" + Mode)];
    /// <summary>Registers per-test services used by this fixture and its lifecycle verification.</summary>
    /// <param name="services">Collection receiving per-test dependency registrations.</param>
    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddScoped(_ => new CleanupProbe(Mode));

    /// <summary>Resolves the cleanup probe and injects the selected setup failure.</summary>
    [SetUp]
    public void DerivedSetup()
    {
        Services.GetRequiredService<CleanupProbe>();
        if (Mode == "setup") throw new InvalidOperationException("intentional-setup");
    }

    /// <summary>Produces the selected intentional body failure while other modes fail in lifecycle callbacks.</summary>
    /// <param name="mode">Intentional lifecycle failure stage selected by the test case.</param>
    [TestCase("setup")]
    [TestCase("body")]
    [TestCase("teardown")]
    [TestCase("cleanup")]
    public void ExpectedFailure(string mode)
    {
        if (mode == "body") Assert.Fail("intentional-body");
    }

    /// <summary>Injects the selected derived teardown failure for the external verifier.</summary>
    [TearDown]
    public void DerivedTeardown()
    {
        if (Mode == "teardown") throw new InvalidOperationException("intentional-teardown");
    }

    /// <summary>Checks quarantine and recovery behavior, then proves every simulated resource is reusable.</summary>
    /// <returns>A task completing after quarantine recovery and successful resource reacquisition.</returns>
    [OneTimeTearDown]
    public static async Task VerifyResourcesReleased()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        foreach (var mode in new[] { "setup", "body", "teardown", "cleanup" })
        {
            var name = "FailureProbe/" + mode;
            if (mode is "teardown" or "cleanup")
            {
                await Assert.ThrowsAsync<ResourceQuarantinedException>(async () =>
                    await ResourceCoordinator.Shared.AcquireAsync([new(name)], timeout.Token));
                ResourceCoordinator.Shared.ResetQuarantine(name, "Verified simulation recovery.");
            }
            using var lease = await ResourceCoordinator.Shared.AcquireAsync([new(name)], timeout.Token);
        }
        File.WriteAllText(Path.Combine(OutputDirectory, "released.txt"), "released");
    }

    /// <summary>Gets the verifier-owned temporary directory for independent cleanup evidence.</summary>
    /// <value>The temporary evidence directory supplied by the probe verifier.</value>
    private static string OutputDirectory => Environment.GetEnvironmentVariable("TESTHARBOR_PROBE_OUTPUT")
        ?? throw new InvalidOperationException("Use the failure-probe verifier.");

    /// <summary>Records lease ownership during cleanup and optionally produces the intentional cleanup failure.</summary>
    /// <param name="mode">Intentional lifecycle failure stage selected by the test case.</param>
    private sealed class CleanupProbe(string mode) : IAsyncDisposable
    {
        /// <summary>Records retained ownership and optionally throws the intentional cleanup failure.</summary>
        /// <returns>A value task completing when cleanup finishes; cleanup failures propagate to the caller.</returns>
        public async ValueTask DisposeAsync()
        {
            using var cancellation = new CancellationTokenSource();
            var waiter = ResourceCoordinator.Shared.AcquireAsync([new("FailureProbe/" + mode)], cancellation.Token);
            var status = ResourceCoordinator.Shared.GetSnapshot().Single(x => x.Name == "FailureProbe/" + mode);
            var retained = status.Exclusive;
            cancellation.Cancel();
            try { (await waiter).Dispose(); } catch (OperationCanceledException) { } catch (ResourceQuarantinedException) { }
            File.WriteAllText(Path.Combine(OutputDirectory, mode + ".txt"), retained ? "cleanup-held-lease" : "lost-lease");
            if (mode == "cleanup") throw new InvalidOperationException("intentional-cleanup");
        }
    }
}
