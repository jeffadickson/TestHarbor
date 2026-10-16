using NUnit.Framework;
using TestHarbor.NUnit.Injection;

namespace TestHarbor.Example.Injection;

/// <summary>Supplies an inherited test contract using explicit constructor dependencies.</summary>
/// <param name="orders">Attempt-local dependency passed by the derived constructor.</param>
public abstract class InjectionContract(OrderStore orders)
{
    /// <summary>Verifies inherited method injection uses the concrete fixture's session.</summary>
    /// <param name="sameStore">The same repository supplied to the base constructor.</param>
    [Test]
    public void Inherited([FromServices] OrderStore sameStore)
    { Assert.That(sameStore, Is.SameAs(orders)); orders.Record("inherited-body"); }
}

/// <summary>Runs an inherited NUnit contract through an injected concrete fixture.</summary>
/// <param name="orders">Repository supplied after extension startup.</param>
[HarborInjectedFixture(typeof(OrderProfile))]
public sealed class InjectionContractTests(OrderStore orders) : InjectionContract(orders);

/// <summary>Checks invalid method binding remains a discoverable failure rather than disappearing.</summary>
[HarborInjectedFixture(typeof(OrderProfile)), Explicit("Negative injection discovery probe.")]
public sealed class InjectionInvalidSignature
{
    /// <summary>Declares an unsupported ref parameter that must be rejected before execution.</summary>
    /// <param name="value">Unsupported by-reference dependency.</param>
    [Test] public void InvalidRef([FromServices] ref OrderStore value) => Assert.Fail("unreachable-body");
}

/// <summary>Checks conflicting lifetime declarations are explicitly rejected.</summary>
[HarborInjectedFixture(typeof(OrderProfile)), FixtureLifeCycle(LifeCycle.SingleInstance), Explicit("Negative injection discovery probe.")]
public sealed class InjectionInvalidLifetime
{
    /// <summary>Must not execute because lifetime validation fails.</summary>
    [Test] public void InvalidLifetime() => Assert.Fail("unreachable-body");
}

/// <summary>Checks constructor ambiguity is rejected before runtime activation.</summary>
[HarborInjectedFixture(typeof(OrderProfile)), Explicit("Negative injection discovery probe.")]
public sealed class InjectionInvalidConstructors
{
    /// <summary>Provides one of two conflicting public constructors.</summary>
    public InjectionInvalidConstructors() { }
    /// <summary>Provides the second conflicting public constructor.</summary>
    /// <param name="store">A dependency that must never be resolved.</param>
    public InjectionInvalidConstructors(OrderStore store) { }
    /// <summary>Must not execute because constructor selection is ambiguous.</summary>
    [Test] public void InvalidConstructors() => Assert.Fail("unreachable-body");
}
