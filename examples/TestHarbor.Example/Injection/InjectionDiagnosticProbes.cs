using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor.NUnit.Injection;

namespace TestHarbor.Example.Injection;

/// <summary>Exercises resolution diagnostics through the actual NUnit method-binding path.</summary>
[HarborInjectedFixture(typeof(DiagnosticProfile)), Explicit("Verified by verify_injection.py.")]
public sealed class InjectionDiagnosticProbes
{
    /// <summary>Must clean an earlier element when a later collection factory fails.</summary>
    /// <param name="service">Collection containing a failing factory after a disposable element.</param>
    [Test] public void CollectionFails([FromServices] IEnumerable<ICollectionProbe> service) => Assert.Fail("unreachable-body");

    /// <summary>Must reject a null collection element and clean already-created elements.</summary>
    /// <param name="service">Collection containing a null factory result.</param>
    [Test] public void CollectionNull([FromServices] IEnumerable<ICollectionProbe> service) => Assert.Fail("unreachable-body");

    /// <summary>Must report the missing dependency below the injected service.</summary>
    /// <param name="service">Service with an unregistered constructor dependency.</param>
    [Test] public void NestedMissing([FromServices] BrokenGraph service) => Assert.Fail("unreachable-body");

    /// <summary>Must retain the container's circular dependency explanation.</summary>
    /// <param name="service">Root of the circular graph.</param>
    [Test] public void Circular([FromServices] CircularGraph<int> service) => Assert.Fail("unreachable-body");

    /// <summary>Must preserve the original factory error and add method context.</summary>
    /// <param name="service">Service whose factory throws.</param>
    [Test] public void FactoryFails([FromServices] FactoryService service) => Assert.Fail("unreachable-body");

    /// <summary>Must distinguish a null factory result from an absent registration.</summary>
    /// <param name="service">Service whose factory returns null.</param>
    [Test] public void FactoryNull([FromServices] NullService service) => Assert.Fail("unreachable-body");

    /// <summary>Must preserve NUnit's skip outcome instead of wrapping it as a configuration failure.</summary>
    /// <param name="service">Service whose factory skips the test.</param>
    [Test] public void FactorySkips([FromServices] FactoryService service) => Assert.Fail("unreachable-body");
}

/// <summary>Exercises contextual errors before fixture construction has completed.</summary>
[HarborInjectedFixture(typeof(DiagnosticProfile)), Explicit("Verified by verify_injection.py.")]
public sealed class InjectionConstructorDiagnostic
{
    /// <summary>Must never be entered because resolution fails first.</summary>
    /// <param name="service">Service with a missing transitive dependency.</param>
    public InjectionConstructorDiagnostic(BrokenGraph service) => Assert.Fail("unreachable-constructor");

    /// <summary>Must not run after constructor dependency resolution fails.</summary>
    [Test] public void ConstructorGraphFails() => Assert.Fail("unreachable-body");
}

/// <summary>Registers faulty graphs while retaining the existing lifecycle evidence extension.</summary>
public sealed class DiagnosticProfile : OrderProfile
{
    /// <summary>Registers graph and factory failures without triggering them during discovery.</summary>
    /// <param name="services">Attempt-local registrations.</param>
    public override void Configure(IServiceCollection services)
    {
        base.Configure(services);
        services.AddScoped<ICollectionProbe, CollectionProbe>();
        services.AddScoped<ICollectionProbe>(_ =>
            TestContext.CurrentContext.Test.MethodName == "CollectionNull" ? null! :
                throw new IOException("collection-error-marker"));
        services.AddScoped<BrokenGraph>(provider => ActivatorUtilities.CreateInstance<BrokenGraph>(provider));
        services.AddScoped(typeof(CircularGraph<>));
        services.AddScoped<NullService>(_ => null!);
        services.AddScoped<FactoryService>(_ =>
        {
            if (TestContext.CurrentContext.Test.MethodName == "FactorySkips") Assert.Ignore("factory-skip-marker");
            throw new IOException("factory-error-marker");
        });
    }
}

/// <summary>Requires a deliberately unregistered dependency.</summary>
public sealed class BrokenGraph
{
    /// <summary>Defines the dependency edge that the container cannot satisfy.</summary>
    /// <param name="missing">Unregistered dependency.</param>
    public BrokenGraph(MissingDependency missing) { }
}

/// <summary>Defines an open generic cycle checked when the closed service is requested.</summary>
/// <typeparam name="T">Type closing the dependency graph at resolution time.</typeparam>
public sealed class CircularGraph<T>
{
    /// <summary>Requires itself so the container rejects a cycle.</summary>
    /// <param name="self">Impossible recursive constructor dependency.</param>
    public CircularGraph(CircularGraph<T> self) { }
}

/// <summary>Identifies a service created by a failing or skipping factory.</summary>
public sealed class FactoryService;

/// <summary>Identifies a service whose registered factory incorrectly returns null.</summary>
public sealed class NullService;

/// <summary>Identifies collection entries used by deliberate failure probes.</summary>
public interface ICollectionProbe;

/// <summary>Records disposal of a collection element created before a later resolution failure.</summary>
/// <param name="store">Existing lifecycle evidence recorder.</param>
public sealed class CollectionProbe(OrderStore store) : ICollectionProbe, IDisposable
{
    /// <summary>Records cleanup independently of whether the collection reached the test body.</summary>
    public void Dispose() => store.Record("collection-dispose");
}
