using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor.NUnit;

namespace TestHarbor.Tests;

// Deliberate failures are opt-in and validated by tools/verify_failure_probes.py.
[Explicit("Run with the failure-probe verifier; all four cases intentionally fail.")]
public sealed class LifecycleFailureProbes : HarborFixture
{
    private string Mode => (string)TestContext.CurrentContext.Test.Arguments[0]!;
    protected override IEnumerable<ResourceRequest> Resources => [new("FailureProbe/" + Mode)];
    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddScoped(_ => new CleanupProbe(Mode));

    [SetUp]
    public void DerivedSetup()
    {
        Services.GetRequiredService<CleanupProbe>();
        if (Mode == "setup") throw new InvalidOperationException("intentional-setup");
    }

    [TestCase("setup")]
    [TestCase("body")]
    [TestCase("teardown")]
    [TestCase("cleanup")]
    public void ExpectedFailure(string mode)
    {
        if (mode == "body") Assert.Fail("intentional-body");
    }

    [TearDown]
    public void DerivedTeardown()
    {
        if (Mode == "teardown") throw new InvalidOperationException("intentional-teardown");
    }

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

    private static string OutputDirectory => Environment.GetEnvironmentVariable("TESTHARBOR_PROBE_OUTPUT")
        ?? throw new InvalidOperationException("Use the failure-probe verifier.");

    private sealed class CleanupProbe(string mode) : IAsyncDisposable
    {
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
