using NUnit.Framework;
using TestHarbor.NUnit.Injection;

namespace TestHarbor.Example.Injection;

/// <summary>Defines a reusable generic-method contract on a closed generic base fixture.</summary>
/// <typeparam name="TCategory">Repository type selected by the concrete fixture.</typeparam>
/// <param name="category">Constructor-injected category repository.</param>
public abstract class GenericMethodContract<TCategory>(GenericRepository<TCategory> category)
{
    /// <summary>Combines a method-selected type with a type selected by the base fixture.</summary>
    /// <typeparam name="TValue">Ordinary case data type.</typeparam>
    /// <param name="value">Application case value.</param>
    /// <param name="values">Method-selected repository.</param>
    /// <param name="sameCategory">Base-fixture-selected repository.</param>
    [TestCase(7), TestCase("book")]
    public void InheritedGeneric<TValue>(TValue value, [FromServices] GenericRepository<TValue> values,
        [FromServices] GenericRepository<TCategory> sameCategory)
    {
        Assert.That(sameCategory, Is.SameAs(category));
        Assert.That(values, Is.Not.SameAs(category));
        values.Values.Add(value);
        Assert.That(values.Values.Single(), Is.EqualTo(value));
    }
}

/// <summary>Exercises inherited generic methods without requiring an open generic fixture.</summary>
/// <param name="category">Attempt-local decimal repository passed to the base contract.</param>
[HarborInjectedFixture(typeof(GenericMethodProfile))]
public sealed class GenericMethodContractTests(GenericRepository<decimal> category) : GenericMethodContract<decimal>(category);
