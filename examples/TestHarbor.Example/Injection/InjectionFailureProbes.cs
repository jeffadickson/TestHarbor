using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor.NUnit.Injection;

namespace TestHarbor.Example.Injection;

/// <summary>Provides deliberate failures verified from external runner results and independent lifecycle files.</summary>
[HarborInjectedFixture(typeof(FailureProfile))]
[Explicit("Run tools/verify_injection.py; failures and non-passing outcomes are intentional.")]
public sealed class InjectionFailureProbes : IAsyncDisposable
{
    /// <summary>Retains the attempt's scoped repository for lifecycle checks.</summary>
    private readonly OrderStore orders;

    /// <summary>Records construction and optionally fails after dependency creation.</summary>
    /// <param name="orders">Repository already started by the extension.</param>
    public InjectionFailureProbes(OrderStore orders)
    {
        this.orders = orders;
        orders.Record("constructor");
        if (CurrentMethod is nameof(ConstructorFails) or nameof(ConstructorAndCleanupFail)) throw new InvalidOperationException("constructor-marker");
    }

    /// <summary>Gets the exact currently executing method name without overload lookup.</summary>
    /// <value>The NUnit case's method name.</value>
    internal static string? CurrentMethod => TestContext.CurrentContext.Test.MethodName;

    /// <summary>Records setup and injects an optional setup failure.</summary>
    [SetUp]
    public void Setup()
    {
        orders.Record("setup");
        if (CurrentMethod == nameof(SetupFails)) throw new InvalidOperationException("setup-marker");
    }

    /// <summary>Must never execute because the constructor fails.</summary>
    [Test] public void ConstructorFails() => Assert.Fail("unreachable-body");
    /// <summary>Must never execute because extension startup fails.</summary>
    [Test] public void StartupFails() => Assert.Fail("unreachable-body");
    /// <summary>Must never execute because NUnit setup fails.</summary>
    [Test] public void SetupFails() => Assert.Fail("unreachable-body");
    /// <summary>Reports an assertion failure while cleanup still completes.</summary>
    [Test] public void BodyFails() { orders.Record("body"); Assert.Fail("body-marker"); }
    /// <summary>Passes the body before NUnit teardown fails.</summary>
    [Test] public void TeardownFails() => orders.Record("body");
    /// <summary>Passes the body before asynchronous fixture disposal fails.</summary>
    [Test] public void FixtureDisposeFails() => orders.Record("body");
    /// <summary>Verifies a constructor error survives a simultaneous extension cleanup failure.</summary>
    [Test] public void ConstructorAndCleanupFail() => Assert.Fail("unreachable-body");
    /// <summary>Passes the body before a scoped service fails asynchronous disposal.</summary>
    [Test] public void ServiceDisposeFails() => orders.Record("body");
    /// <summary>Must never execute because cancellation interrupts extension startup.</summary>
    [Test, CancelAfter(100)] public void StartupCancelled() => Assert.Fail("unreachable-body");
    /// <summary>Passes the body before extension cleanup fails.</summary>
    [Test] public void CleanupFails() => orders.Record("body");
    /// <summary>Reports both an assertion failure and an extension cleanup failure.</summary>
    [Test] public void BodyAndCleanupFail() { orders.Record("body"); Assert.Fail("primary-marker"); }
    /// <summary>Fails method binding without entering its body when a service is missing.</summary>
    /// <param name="missing">Unregistered dependency.</param>
    [Test] public void MissingService([FromServices] MissingDependency missing) => Assert.Fail("unreachable-body");
    /// <summary>Fails method binding when the service has multiple registrations.</summary>
    /// <param name="duplicate">Ambiguous dependency.</param>
    [Test] public void AmbiguousService([FromServices] DuplicateDependency duplicate) => Assert.Fail("unreachable-body");
    /// <summary>Preserves NUnit's ignored result while disposing dependencies.</summary>
    [Test] public void Skipped() { orders.Record("body"); Assert.Ignore("skip-marker"); }
    /// <summary>Preserves NUnit's inconclusive result while disposing dependencies.</summary>
    [Test] public void Inconclusive() { orders.Record("body"); Assert.Inconclusive("inconclusive-marker"); }
    /// <summary>Must never execute because a pure profile condition skips before acquisition.</summary>
    [Test] public void ConditionSkips() => Assert.Fail("unreachable-body");
    /// <summary>Observes NUnit cancellation and still runs cleanup with ownership retained.</summary>
    /// <param name="token">Cancellation token supplied by NUnit as its final data parameter.</param>
    /// <returns>A task cancelled by NUnit's cooperative deadline.</returns>
    [Test, CancelAfter(100)]
    public async Task Cancelled(CancellationToken token) { orders.Record("body"); await Task.Delay(Timeout.Infinite, token); }

    /// <summary>Records teardown and injects an optional teardown failure.</summary>
    [TearDown]
    public void Teardown()
    {
        orders.Record("teardown");
        if (CurrentMethod == nameof(TeardownFails)) throw new IOException("teardown-marker");
    }

    /// <summary>Records async fixture disposal before its services are cleaned.</summary>
    /// <returns>An awaitable optionally failing after disposal has been attempted.</returns>
    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        orders.Record("fixture-dispose");
        if (CurrentMethod == nameof(FixtureDisposeFails)) throw new IOException("fixture-dispose-marker");
    }
}

/// <summary>Registers deterministic startup/cleanup faults and an ambiguous service.</summary>
public sealed class FailureProfile : OrderProfile
{
    /// <summary>Skips exactly the condition probe before session acquisition.</summary>
    /// <value>A pure predicate over the current NUnit method identity.</value>
    public override IEnumerable<ExecutionCondition> Conditions =>
        [new("condition-marker", _ => InjectionFailureProbes.CurrentMethod != "ConditionSkips", ConditionDisposition.Skip)];

    /// <summary>Adds fault behavior after the normal repository startup extension.</summary>
    /// <param name="services">The attempt-local service registrations.</param>
    public override void Configure(IServiceCollection services)
    {
        base.Configure(services);
        services.AddScoped<DuplicateDependency>();
        services.AddScoped<DuplicateDependency>();
        services.AddScoped<ITestExtension, FaultExtension>();
    }
}

/// <summary>Names an intentionally unregistered service.</summary>
public sealed class MissingDependency;
/// <summary>Names an intentionally multiply registered service.</summary>
public sealed class DuplicateDependency;

/// <summary>Injects failures while allowing the earlier extension and services to clean up.</summary>
public sealed class FaultExtension : ITestExtension
{
    /// <summary>Optionally fails after the earlier extension has started.</summary>
    /// <param name="context">Current session context.</param>
    /// <param name="cancellationToken">Startup cancellation.</param>
    /// <returns>A completed task, unless startup failure is injected.</returns>
    public async Task StartAsync(TestExtensionContext context, CancellationToken cancellationToken)
    {
        if (InjectionFailureProbes.CurrentMethod == "StartupCancelled") await Task.Delay(Timeout.Infinite, cancellationToken);
        if (InjectionFailureProbes.CurrentMethod == "StartupFails") throw new IOException("startup-marker");
    }

    /// <summary>Optionally fails cleanup so remaining stop/disposal and quarantine can be verified.</summary>
    /// <param name="context">Current session context.</param>
    /// <returns>A completed task, unless cleanup failure is injected.</returns>
    public Task StopAsync(TestExtensionContext context)
    {
        if (InjectionFailureProbes.CurrentMethod is "CleanupFails" or "BodyAndCleanupFail" or "ConstructorAndCleanupFail") throw new IOException("cleanup-marker");
        return Task.CompletedTask;
    }
}
