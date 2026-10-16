using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor.NUnit.Injection;

namespace TestHarbor.Example.Injection;

/// <summary>Defines a software-only checkout validation rule with attempt-local observations.</summary>
public interface ICheckoutRule
{
    /// <summary>Gets the inputs observed by this rule instance.</summary>
    /// <value>Attempt-local quantities already evaluated.</value>
    List<int> Seen { get; }

    /// <summary>Evaluates one requested quantity.</summary>
    /// <param name="quantity">Requested product count.</param>
    /// <returns>Whether this rule permits checkout.</returns>
    bool Accept(int quantity);
}

/// <summary>Requires a positive quantity.</summary>
public sealed class PositiveQuantityRule : ICheckoutRule
{
    /// <summary>Gets quantities observed by this attempt-local rule.</summary>
    /// <value>The mutable observation list, initially empty.</value>
    public List<int> Seen { get; } = [];
    /// <summary>Records and evaluates the requested quantity against this rule.</summary>
    /// <param name="quantity">Requested number of products.</param>
    /// <returns>Whether this rule accepts the quantity.</returns>
    public bool Accept(int quantity) { Seen.Add(quantity); return quantity > 0; }
}

/// <summary>Limits a purchase to five items.</summary>
public sealed class StockLimitRule : ICheckoutRule
{
    /// <summary>Gets quantities observed by this attempt-local rule.</summary>
    /// <value>The mutable observation list, initially empty.</value>
    public List<int> Seen { get; } = [];
    /// <summary>Records and evaluates the requested quantity against this rule.</summary>
    /// <param name="quantity">Requested number of products.</param>
    /// <returns>Whether this rule accepts the quantity.</returns>
    public bool Accept(int quantity) { Seen.Add(quantity); return quantity <= 5; }
}

/// <summary>Represents a transient dependency used to verify collection lifetimes.</summary>
public sealed class CollectionTransient;

/// <summary>Represents an optional extension point with no registrations.</summary>
public interface IOptionalCheckoutRule;

/// <summary>Registers multiple rules under one interface and a transient comparison service.</summary>
public sealed class CollectionProfile : InjectionProfile
{
    /// <summary>Registers ordered scoped rules and a transient service.</summary>
    /// <param name="services">Attempt-local dependency registrations.</param>
    public override void Configure(IServiceCollection services)
    {
        services.AddScoped<ICheckoutRule, PositiveQuantityRule>();
        services.AddScoped<ICheckoutRule, StockLimitRule>();
        services.AddTransient<CollectionTransient>();
    }
}

/// <summary>Receives all rules in a constructor and observes the same scoped elements in methods.</summary>
/// <param name="rules">Ordered scoped rules.</param>
/// <param name="optional">Empty optional extension point.</param>
/// <param name="transients">Constructor-local transient resolution.</param>
[HarborInjectedFixture(typeof(CollectionProfile)), Parallelizable(ParallelScope.All)]
public sealed class InjectionCollectionTests(IEnumerable<ICheckoutRule> rules,
    IEnumerable<IOptionalCheckoutRule> optional, IEnumerable<CollectionTransient> transients)
{
    /// <summary>Verifies business rules, registration order and per-case mutable state.</summary>
    /// <param name="quantity">Ordinary NUnit input.</param>
    /// <param name="methodRules">Scoped rules resolved again for this method.</param>
    /// <param name="accepted">Expected result of all rules.</param>
    [TestCase(0, false), TestCase(3, true), TestCase(6, false)]
    public void ValidatesCheckout(int quantity, [FromServices] IEnumerable<ICheckoutRule> methodRules, bool accepted)
    {
        var constructorRules = rules.ToArray();
        var actual = methodRules.ToArray();
        Assert.That(actual.Select(x => x.GetType()), Is.EqualTo(new[] { typeof(PositiveQuantityRule), typeof(StockLimitRule) }));
        for (var i = 0; i < actual.Length; i++) Assert.That(actual[i], Is.SameAs(constructorRules[i]));
        Assert.That(actual.All(x => x.Seen.Count == 0), Is.True, "Parallel cases must not share rule state.");
        var decisions = actual.Select(x => x.Accept(quantity)).ToArray();
        Assert.That(decisions.All(x => x), Is.EqualTo(accepted));
        Assert.That(constructorRules.All(x => x.Seen.SequenceEqual(new[] { quantity })), Is.True);
        TestContext.Out.WriteLine("TestHarbor injects all registered rules in order. Constructor and method share scoped rule instances; each case gets fresh state.");
    }

    /// <summary>Checks an unregistered extension point is an empty collection at both boundaries.</summary>
    /// <param name="methodOptional">Empty collection supplied to the method.</param>
    [Test]
    public void EmptyCollection([FromServices] IEnumerable<IOptionalCheckoutRule> methodOptional)
    {
        Assert.That(optional, Is.Empty);
        Assert.That(methodOptional, Is.Empty);
    }

    /// <summary>Checks separate collection resolutions preserve transient creation semantics.</summary>
    /// <param name="methodTransients">Fresh transient instances resolved for this method.</param>
    [Test]
    public void TransientElements([FromServices] IEnumerable<CollectionTransient> methodTransients) =>
        Assert.That(methodTransients.Single(), Is.Not.SameAs(transients.Single()));
}
