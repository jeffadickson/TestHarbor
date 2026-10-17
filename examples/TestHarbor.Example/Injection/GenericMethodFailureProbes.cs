using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor.NUnit.Injection;

namespace TestHarbor.Example.Injection;

/// <summary>Provides deliberately invalid closed registrations to exercise generic resolution diagnostics.</summary>
public sealed class GenericFailureProfile : GenericMethodProfile
{
    /// <summary>Adds ambiguous and null closed services alongside valid open registrations.</summary>
    /// <param name="services">Per-attempt registrations.</param>
    public override void Configure(IServiceCollection services)
    {
        base.Configure(services);
        services.AddScoped<GenericRepository<int>>();
        services.AddScoped<GenericRepository<int>>();
        services.AddScoped<INullGeneric<long>>(_ => null!);
    }
}

/// <summary>Represents a missing generic application service.</summary>
/// <typeparam name="T">Case-selected application type.</typeparam>
public interface IMissingGeneric<T>;

/// <summary>Represents a closed generic service with a deliberately null factory.</summary>
/// <typeparam name="T">Explicit application type selecting the factory.</typeparam>
public interface INullGeneric<T>;

/// <summary>Preserves concrete type diagnostics and cleanup when generic method resolution fails.</summary>
/// <param name="marker">Service created before method injection begins.</param>
[HarborInjectedFixture(typeof(GenericFailureProfile)), Explicit("Run generic failure verifier")]
public sealed class GenericMethodFailureProbes(GenericRepository<string> marker)
{
    /// <summary>Rejects a missing closed generic service after partial method resolution.</summary>
    /// <typeparam name="T">Type inferred from ordinary data.</typeparam>
    /// <param name="value">Ordinary case data.</param>
    /// <param name="partial">Successfully resolved service that must be disposed.</param>
    /// <param name="missing">Unregistered closed generic dependency.</param>
    [TestCase(7)]
    public void Missing<T>(T value, [FromServices] GenericRepository<double> partial, [FromServices] IMissingGeneric<T> missing)
    { marker.Values.Add("unreachable"); Assert.Fail("unreachable-body"); }

    /// <summary>Rejects ambiguous closed registrations after constructing the fixture.</summary>
    /// <typeparam name="T">Type whose closed registration is ambiguous.</typeparam>
    /// <param name="value">Ordinary case data.</param>
    /// <param name="partial">Valid dependency resolved first.</param>
    /// <param name="ambiguous">Dependency with duplicate registrations.</param>
    [TestCase(7)]
    public void Ambiguous<T>(T value, [FromServices] GenericRepository<double> partial, [FromServices] GenericRepository<T> ambiguous)
    { marker.Values.Add("unreachable"); Assert.Fail("unreachable-body"); }

    /// <summary>Rejects null from an explicitly closed generic factory.</summary>
    /// <typeparam name="T">Explicit long type selecting the null factory.</typeparam>
    /// <param name="value">Ordinary converted case data.</param>
    /// <param name="partial">Valid dependency resolved first.</param>
    /// <param name="missing">Closed service whose factory returns null.</param>
    [TestCase(7, TypeArgs = new[] { typeof(long) })]
    public void NullFactory<T>(T value, [FromServices] GenericRepository<double> partial, [FromServices] INullGeneric<T> missing)
    { marker.Values.Add("unreachable"); Assert.Fail("unreachable-body"); }
}
