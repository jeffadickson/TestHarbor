using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor.NUnit.Injection;

namespace TestHarbor.Example.Injection;

/// <summary>Verifies failed keyed selections and partial collection cleanup through the real runner.</summary>
[HarborInjectedFixture(typeof(KeyedFailureProfile)), Explicit("Run verify_injection.py.")]
public sealed class KeyedFailureProbes
{
    /// <summary>Requires a missing key without falling back to the default endpoint.</summary>
    /// <param name="service">Unresolvable selected service.</param>
    [Test] public void MissingKey([FromKeyedServices("absent")] KeyedEndpoint service) => Assert.Fail("unreachable-body");
    /// <summary>Rejects two registrations for a singular keyed dependency.</summary>
    /// <param name="service">Ambiguous selected service.</param>
    [Test] public void DuplicateKey([FromKeyedServices("duplicate")] KeyedEndpoint service) => Assert.Fail("unreachable-body");
    /// <summary>Rejects a null keyed factory result.</summary>
    /// <param name="service">Null selected service.</param>
    [Test] public void NullKeyedService([FromKeyedServices("null")] KeyedEndpoint service) => Assert.Fail("unreachable-body");
    /// <summary>Closes earlier collection elements after a later factory throws.</summary>
    /// <param name="service">Partially activated collection.</param>
    [Test] public void KeyedCollectionFails([FromKeyedServices("partial")] IEnumerable<ICollectionProbe> service) => Assert.Fail("unreachable-body");
    /// <summary>Closes earlier collection elements after a later factory returns null.</summary>
    /// <param name="service">Collection containing a null member.</param>
    [Test] public void KeyedCollectionNull([FromKeyedServices("partial")] IEnumerable<ICollectionProbe> service) => Assert.Fail("unreachable-body");
}

/// <summary>Provides keyed faults with independent lifecycle evidence.</summary>
public sealed class KeyedFailureProfile : OrderProfile
{
    /// <summary>Registers successful defaults and deliberately invalid keyed selections.</summary>
    /// <param name="services">Attempt-local registrations.</param>
    public override void Configure(IServiceCollection services)
    {
        base.Configure(services);
        services.AddScoped<KeyedEndpoint>(_ => new("default"));
        services.AddKeyedScoped<KeyedEndpoint>("duplicate", (_, _) => new("one"));
        services.AddKeyedScoped<KeyedEndpoint>("duplicate", (_, _) => new("two"));
        services.AddKeyedScoped<KeyedEndpoint>("null", (_, _) => null!);
        services.AddKeyedScoped<ICollectionProbe>("partial", (provider, _) => ActivatorUtilities.CreateInstance<CollectionProbe>(provider));
        services.AddKeyedScoped<ICollectionProbe>("partial", (_, _) =>
            TestContext.CurrentContext.Test.MethodName == "KeyedCollectionNull" ? null! : throw new IOException("keyed-collection-marker"));
    }
}

/// <summary>Must be invalid before the profile or constructor can be activated.</summary>
[HarborInjectedFixture(typeof(KeyedProfile)), Explicit("Invalid keyed declaration probe.")]
public sealed class KeyedInvalidConflict
{
    /// <summary>Rejects conflicting selection markers on one method parameter.</summary>
    /// <param name="service">Conflicting keyed and unkeyed selection.</param>
    [Test] public void Conflicting([FromServices, FromKeyedServices("primary")] KeyedEndpoint service) => Assert.Fail("unreachable-body");
}

/// <summary>Rejects implicit parent-key inheritance at the fixture boundary.</summary>
[HarborInjectedFixture(typeof(KeyedProfile)), Explicit("Invalid keyed declaration probe.")]
public sealed class KeyedInvalidInheritance
{
    /// <summary>Must not execute because a fixture has no parent service key.</summary>
    /// <param name="service">Invalid inherited key selection.</param>
    public KeyedInvalidInheritance([FromKeyedServices] KeyedEndpoint service) => Assert.Fail("unreachable-constructor");
    /// <summary>Must not run with an invalid constructor.</summary>
    [Test] public void InheritedKey() => Assert.Fail("unreachable-body");
}

/// <summary>Rejects requesting a nonexistent parent service key as ordinary data.</summary>
[HarborInjectedFixture(typeof(KeyedProfile)), Explicit("Invalid keyed declaration probe.")]
public sealed class KeyedInvalidServiceKey
{
    /// <summary>Must fail discovery with an actionable unsupported-marker explanation.</summary>
    /// <param name="key">No parent key exists for the test method.</param>
    [TestCase("data")] public void ParentKey([ServiceKey] string key) => Assert.Fail("unreachable-body");
}
