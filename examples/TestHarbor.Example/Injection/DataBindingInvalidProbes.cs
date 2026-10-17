using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor.NUnit.Injection;

namespace TestHarbor.Example.Injection;

/// <summary>Rejects data sources hidden behind ordinary method injection.</summary>
[HarborInjectedFixture(typeof(KeyedProfile)), Explicit("Run injection data verifier.")]
public sealed class DataBindingInvalidService
{
    /// <summary>Must fail discovery because the parameter has contradictory roles.</summary>
    /// <param name="service">An injected service incorrectly marked as data.</param>
    [Test] public void ConflictingData([FromServices, Values(null)] KeyedEndpoint service) => Assert.Fail("unreachable-body");
}

/// <summary>Rejects data sources hidden behind keyed method injection.</summary>
[HarborInjectedFixture(typeof(KeyedProfile)), Explicit("Run injection data verifier.")]
public sealed class DataBindingInvalidKeyed
{
    /// <summary>Must fail discovery rather than silently ignoring the Values declaration.</summary>
    /// <param name="service">A keyed dependency incorrectly marked as data.</param>
    [Test] public void KeyedData([FromKeyedServices("primary"), Values(null)] KeyedEndpoint service) => Assert.Fail("unreachable-body");
}

/// <summary>Rejects constructor data attributes because constructor parameters are always services.</summary>
[HarborInjectedFixture(typeof(KeyedProfile)), Explicit("Run injection data verifier.")]
public sealed class DataBindingInvalidConstructor
{
    /// <summary>Must not execute with an unsupported data binding declaration.</summary>
    /// <param name="service">Constructor service incorrectly declaring test data.</param>
    public DataBindingInvalidConstructor([Values(null)] KeyedEndpoint service) => Assert.Fail("unreachable-constructor");
    /// <summary>Must not execute after invalid construction metadata.</summary>
    [Test] public void ConstructorData() => Assert.Fail("unreachable-body");
}
