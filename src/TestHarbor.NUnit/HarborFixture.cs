using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace TestHarbor.NUnit;

/// <summary>Adds a TestHarbor session around each NUnit test using public lifecycle hooks only.</summary>
/// <remarks>Keep InstancePerTestCase lifecycle. Do not hide the lifecycle methods.
/// Constructors and OneTimeSetUp are outside resource ownership. Resolve services in SetUp or tests.</remarks>
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public abstract class HarborFixture
{
    private TestSession? session;

    /// <summary>Gets the current test's services, available through derived TearDown.</summary>
    protected IServiceProvider Services => session?.Services
        ?? throw new InvalidOperationException("Services are available only during the per-test lifecycle.");

    /// <summary>Declares all resources this test will need; may inspect public NUnit TestContext.</summary>
    protected virtual IEnumerable<ResourceRequest> Resources => [];

    /// <summary>Registers this test's services; singleton registrations are session-local.</summary>
    /// <param name="services">Registration collection owned by this test session.</param>
    protected virtual void ConfigureServices(IServiceCollection services) { }

    /// <summary>Acquires the session before derived setup methods run.</summary>
    [SetUp]
    public async Task OpenHarborSessionAsync()
    {
        session = await TestSession.OpenAsync(Resources, ConfigureServices,
            cancellationToken: TestContext.CurrentContext.CancellationToken,
            options: new TestSessionOptions { Owner = Owner });
    }

    private static string Owner => $"{TestContext.CurrentContext.Test.ID}: {TestContext.CurrentContext.Test.FullName}";

    /// <summary>Awaits service cleanup after derived teardown methods complete.</summary>
    [TearDown]
    public async Task CloseHarborSessionAsync()
    {
        var current = session;
        session = null;
        if (current is null) return;
        try
        {
            if (TestContext.CurrentContext.Result.Outcome.Site == FailureSite.TearDown)
                current.QuarantineResources("NUnit derived TearDown failed: " + TestContext.CurrentContext.Result.Message);
        }
        finally
        {
            try { await current.DisposeAsync(); }
            finally
            {
                foreach (var item in ResourceCoordinator.Shared.GetEvents().Where(x => x.Owner == Owner))
                    TestContext.Progress.WriteLine($"[TestHarbor] {item.Kind}: {item.Resources} ({item.Elapsed.TotalMilliseconds:F1} ms)");
            }
        }
    }
}
