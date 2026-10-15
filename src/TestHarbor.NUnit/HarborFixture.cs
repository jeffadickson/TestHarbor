using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace TestHarbor.NUnit;

/// <summary>Adds a TestHarbor session around each NUnit test using public lifecycle hooks only.</summary>
/// <remarks>Keep InstancePerTestCase lifecycle. Do not hide the lifecycle methods.
/// Constructors and OneTimeSetUp are outside resource ownership. Resolve services in SetUp or tests.</remarks>
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public abstract class HarborFixture
{
    /// <summary>Rejects incompatible fixture lifecycle declarations before NUnit can run hidden callbacks.</summary>
    /// <remarks>Only user fixture metadata is inspected; NUnit internals are never reflected.</remarks>
    protected HarborFixture()
    {
        var type = GetType();
        if (type.GetCustomAttribute<FixtureLifeCycleAttribute>(true)?.LifeCycle != LifeCycle.InstancePerTestCase)
            throw new InvalidOperationException("HarborFixture requires InstancePerTestCase.");
        for (var current = type; current != typeof(HarborFixture) && current is not null; current = current.BaseType)
            if (current.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(m => m.Name is nameof(OpenHarborSessionAsync) or nameof(CloseHarborSessionAsync)))
                throw new InvalidOperationException("Do not hide HarborFixture lifecycle methods.");
    }

    /// <summary>Holds the active per-test session between base setup and base teardown.</summary>
    private TestSession? session;

    /// <summary>Gets the current test's services, available through derived TearDown.</summary>
    /// <value>The per-test service provider; unavailable outside the active session.</value>
    protected IServiceProvider Services => session?.Services
        ?? throw new InvalidOperationException("Services are available only during the per-test lifecycle.");

    /// <summary>Declares all resources this test will need; may inspect public NUnit TestContext.</summary>
    /// <value>Additional resource requirements, merged with inherited RequiresResource declarations.</value>
    protected virtual IEnumerable<ResourceRequest> Resources => [];

    /// <summary>Builds typed configuration before conditions or resource acquisition.</summary>
    /// <returns>The configuration snapshot for this test.</returns>
    protected virtual TestConfiguration ConfigureTest() => new([]);

    /// <summary>Gets pure execution conditions evaluated in declaration order before resource acquisition.</summary>
    /// <value>The explicitly selected conditions for this test.</value>
    protected virtual IEnumerable<ExecutionCondition> Conditions => [];

    /// <summary>Gets session options including explicit shared provider ownership and phase observations.</summary>
    /// <value>Options for this session; the NUnit owner identity is always supplied by the wrapper.</value>
    protected virtual TestSessionOptions SessionOptions => new();

    /// <summary>Registers this test's services; singleton registrations are session-local.</summary>
    /// <param name="services">Registration collection owned by this test session.</param>
    protected virtual void ConfigureServices(IServiceCollection services) { }

    /// <summary>Acquires the session before derived setup methods run.</summary>
    /// <returns>A task completing when the session is ready for derived setup.</returns>
    [SetUp]
    public async Task OpenHarborSessionAsync()
    {
        var configuration = ConfigureTest();
        TestContext.Out.WriteLine("[TestHarbor] configuration: " + System.Text.Json.JsonSerializer.Serialize(configuration.Describe()));
        foreach (var condition in Conditions)
        {
            if (!Enum.IsDefined(condition.Disposition)) throw new ArgumentException("Unknown condition disposition.");
            ArgumentException.ThrowIfNullOrWhiteSpace(condition.Reason);
            if (condition.Predicate(configuration)) continue;
            switch (condition.Disposition)
            {
                case ConditionDisposition.Skip: Assert.Ignore(condition.Reason); break;
                case ConditionDisposition.Inconclusive: Assert.Inconclusive(condition.Reason); break;
                case ConditionDisposition.Fail: Assert.Fail(condition.Reason); break;
            }
        }
        var options = SessionOptions with { Owner = Owner };
        var profiles = UseExtensionProfileAttribute.CurrentProfiles();
        if (options.ServiceScope is not null && GetType().GetMethod(nameof(ConfigureServices), BindingFlags.Instance | BindingFlags.NonPublic)!.DeclaringType != typeof(HarborFixture))
            throw new InvalidOperationException("Configure services on the shared owner instead of overriding ConfigureServices.");
        if (options.ServiceScope is not null && profiles.Count > 0)
            throw new InvalidOperationException("Apply profiles when constructing a shared service owner; per-test profiles cannot change its registrations.");
        session = await TestSession.OpenAsync(Resources.Concat(RequiresResourceAttribute.CurrentRequirements()), options.ServiceScope is null ? services =>
            {
                foreach (var profile in profiles) profile.Configure(services);
                ConfigureServices(services);
            } : null,
            cancellationToken: TestContext.CurrentContext.CancellationToken,
            options: options);
    }

    /// <summary>Gets the label used to correlate this operation with diagnostic events.</summary>
    /// <value>The diagnostic owner label for this request or session.</value>
    private static string Owner => $"{TestContext.CurrentContext.Test.ID}: {TestContext.CurrentContext.Test.FullName}";

    /// <summary>Awaits service cleanup after derived teardown methods complete.</summary>
    /// <returns>A task completing after session cleanup and diagnostic output.</returns>
    [TearDown]
    public async Task CloseHarborSessionAsync()
    {
        var current = session;
        session = null;
        if (current is null) return;
        try
        {
            if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed)
                current.RecordFailure(new InvalidOperationException("NUnit provisional failure: " + TestContext.CurrentContext.Result.Message));
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
