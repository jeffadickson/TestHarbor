using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor.NUnit.Injection;

namespace TestHarbor.Example.Injection;

/// <summary>Checks ordinary NUnit argument preparation alongside execution-time services.</summary>
/// <param name="endpoint">Attempt-local endpoint shared with method injection.</param>
/// <param name="constructorProvider">Provider owning the constructor dependencies.</param>
[HarborInjectedFixture(typeof(KeyedProfile))]
public sealed class InjectionArgumentTests(KeyedEndpoint endpoint, IServiceProvider constructorProvider)
{
    /// <summary>Uses an omitted ordinary argument's default without defaulting the service.</summary>
    /// <param name="same">Endpoint resolved from the fixture's scope.</param>
    /// <param name="quantity">Optional data default supplied by NUnit.</param>
    [TestCase]
    public void OptionalDefault([FromServices] KeyedEndpoint same, int quantity = 7)
    { Assert.That(same, Is.SameAs(endpoint)); Assert.That(quantity, Is.EqualTo(7)); }

    /// <summary>Explicit case data overrides an optional default.</summary>
    /// <param name="same">Scoped service before optional data.</param>
    /// <param name="quantity">Explicit quantity supplied by NUnit.</param>
    [TestCase(9)]
    public void OptionalExplicit([FromServices] KeyedEndpoint same, int quantity = 7)
    { Assert.That(same, Is.SameAs(endpoint)); Assert.That(quantity, Is.EqualTo(9)); }

    /// <summary>Preserves a null optional data value after a keyed dependency.</summary>
    /// <param name="primary">Keyed dependency.</param>
    /// <param name="quantity">Omitted nullable data.</param>
    [TestCase]
    public void NullableDefault([FromKeyedServices("primary")] KeyedEndpoint primary, int? quantity = null)
    { Assert.That(primary.Name, Is.EqualTo("primary")); Assert.That(quantity, Is.Null); }

    /// <summary>Packs zero, one and multiple ordinary arguments into a params array.</summary>
    /// <param name="same">Scoped dependency excluded from array packing.</param>
    /// <param name="values">NUnit-packed data array.</param>
    [TestCase, TestCase(1), TestCase(1, 2, 3)]
    public void ParamsData([FromServices] KeyedEndpoint same, params int[] values)
    { Assert.That(same, Is.SameAs(endpoint)); Assert.That(values, Is.EqualTo(Enumerable.Range(1, values.Length))); }

    /// <summary>Packs multiple data arguments into NUnit's single object-array convention.</summary>
    /// <param name="same">Scoped service excluded from array packing.</param>
    /// <param name="row">Packed mixed-type data.</param>
    [TestCase(3, "book")]
    public void ObjectArray([FromServices] KeyedEndpoint same, object[] row)
    { Assert.That(same, Is.SameAs(endpoint)); Assert.That(row, Is.EqualTo(new object[] { 3, "book" })); }

    /// <summary>Gets quantities without resolving runtime services.</summary>
    /// <value>Two ordinary data values.</value>
    public static int[] Quantities => [2, 4];

    /// <summary>Combines a property ValueSource with injection.</summary>
    /// <param name="quantity">Source-provided quantity.</param>
    /// <param name="same">Attempt-local service.</param>
    [Test]
    public void ValueSource([ValueSource(nameof(Quantities))] int quantity, [FromServices] KeyedEndpoint same)
    { Assert.That(quantity, Is.AnyOf(2, 4)); Assert.That(same, Is.SameAs(endpoint)); }

    /// <summary>Uses a source declared on a separate type.</summary>
    /// <param name="same">Execution-time service.</param>
    /// <param name="quantity">Data obtained independently of the fixture.</param>
    [TestCaseSource(typeof(ArgumentRows), nameof(ArgumentRows.External))]
    public void ExternalSource([FromServices] KeyedEndpoint same, int quantity)
    { Assert.That(quantity, Is.EqualTo(6)); Assert.That(same, Is.SameAs(endpoint)); }

    /// <summary>Passes source arguments through NUnit before runtime service binding.</summary>
    /// <param name="quantity">Computed source data.</param>
    /// <param name="same">Runtime service.</param>
    [TestCaseSource(typeof(ArgumentRows), nameof(ArgumentRows.Multiplied), new object[] { 3 })]
    public void ParameterizedSource(int quantity, [FromServices] KeyedEndpoint same)
    { Assert.That(quantity, Is.EqualTo(12)); Assert.That(same, Is.SameAs(endpoint)); }

    /// <summary>Keeps an array-valued row as one ordinary parameter.</summary>
    /// <param name="values">Array carried by TestCaseData.</param>
    /// <param name="same">Service following array data.</param>
    [TestCaseSource(typeof(ArgumentRows), nameof(ArgumentRows.Arrays))]
    public void ArraySource(int[] values, [FromServices] KeyedEndpoint same)
    { Assert.That(values, Is.EqualTo(new[] { 2, 4 })); Assert.That(same, Is.SameAs(endpoint)); }

    /// <summary>Retains expected results declared by a source.</summary>
    /// <param name="same">Dependency preceding source data.</param>
    /// <param name="quantity">Source quantity.</param>
    /// <returns>The software order total checked by NUnit.</returns>
    [TestCaseSource(typeof(ArgumentRows), nameof(ArgumentRows.Results))]
    public decimal SourceResult([FromServices] KeyedEndpoint same, int quantity)
    { Assert.That(same, Is.SameAs(endpoint)); return quantity * 1.25m; }

    /// <summary>Retains explicit case names and category metadata on injected methods.</summary>
    /// <param name="same">Dependency excluded from the public case name.</param>
    [TestCase(TestName = "NamedCategory", Category = "InjectionMetadata")]
    public void NamedCategory([FromServices] KeyedEndpoint same)
    { Assert.That(same, Is.SameAs(endpoint)); Assert.That(TestContext.CurrentContext.Test.Name, Is.EqualTo("NamedCategory")); }

    /// <summary>Preserves NUnit culture control while converting decimal case data.</summary>
    /// <param name="amount">Decimal converted at discovery.</param>
    /// <param name="same">Attempt-local service.</param>
    [TestCase(1.25), SetCulture("fr-FR")]
    public void Culture(decimal amount, [FromServices] KeyedEndpoint same)
    {
        Assert.That(amount, Is.EqualTo(1.25m));
        Assert.That(System.Globalization.CultureInfo.CurrentCulture.Name, Is.EqualTo("fr-FR"));
        Assert.That(same, Is.SameAs(endpoint));
    }

    /// <summary>Preserves enum-valued case metadata while interleaving a service.</summary>
    /// <param name="mode">Ordinary enum data.</param>
    /// <param name="same">Scoped service.</param>
    [TestCase(ShippingMode.Express)]
    public void EnumData(ShippingMode mode, [FromServices] KeyedEndpoint same)
    { Assert.That(mode, Is.EqualTo(ShippingMode.Express)); Assert.That(same, Is.SameAs(endpoint)); }

    /// <summary>Lets NUnit supply its implicit cancellation token after generated data.</summary>
    /// <param name="same">Service preceding the ordinary data signature.</param>
    /// <param name="quantity">Generated order quantity.</param>
    /// <param name="token">NUnit-owned test cancellation token.</param>
    [TestCase(2)]
    public void Cancellation([FromServices] KeyedEndpoint same, int quantity, CancellationToken token)
    { Assert.That(quantity, Is.EqualTo(2)); Assert.That(same, Is.SameAs(endpoint)); Assert.That(token, Is.EqualTo(TestContext.CurrentContext.CancellationToken)); }

    /// <summary>Resolves the current scope provider without requiring a redundant registration.</summary>
    /// <param name="provider">Microsoft DI's built-in scoped provider.</param>
    [Test]
    public void Provider([FromServices] IServiceProvider provider)
    { Assert.That(provider, Is.SameAs(constructorProvider)); Assert.That(provider.GetRequiredService<KeyedEndpoint>(), Is.SameAs(endpoint)); }

    /// <summary>A deliberately created child scope has distinct service state and preserves parent identity.</summary>
    /// <param name="provider">Current attempt's provider.</param>
    [Test]
    public void ChildScope([FromServices] IServiceProvider provider)
    {
        using var child = provider.CreateScope();
        var other = child.ServiceProvider.GetRequiredService<KeyedEndpoint>();
        Assert.That(other, Is.Not.SameAs(endpoint));
        Assert.That(child.ServiceProvider.GetRequiredService<KeyedEndpoint>(), Is.SameAs(other));
        Assert.That(provider.GetRequiredService<KeyedEndpoint>(), Is.SameAs(endpoint));
    }

    /// <summary>Lets Microsoft DI select a keyed dependency inside an application service.</summary>
    /// <param name="order">Application service constructed by DI.</param>
    /// <param name="primary">The matching keyed endpoint injected directly.</param>
    [Test]
    public void TransitiveKey([FromServices] KeyedOrder order, [FromKeyedServices("primary")] KeyedEndpoint primary)
    {
        Assert.That(order.Endpoint, Is.SameAs(primary));
        Assert.That(order.Endpoint, Is.Not.SameAs(endpoint));
        TestContext.Out.WriteLine("TestHarbor supplies the application service and the keyed endpoint from one attempt scope; the test needs no manual provider setup or cleanup. NUnit still owns the assertions and case execution.");
    }

}
