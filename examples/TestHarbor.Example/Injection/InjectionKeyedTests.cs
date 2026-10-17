using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor.NUnit.Injection;

namespace TestHarbor.Example.Injection;

/// <summary>Represents an attempt-local software endpoint selected by registration key.</summary>
/// <param name="name">Human-readable endpoint role.</param>
public sealed class KeyedEndpoint(string name)
{
    /// <summary>Gets the selected endpoint role.</summary>
    /// <value>The role configured by the test profile.</value>
    public string Name { get; } = name;

    /// <summary>Gets request observations for this test attempt.</summary>
    /// <value>An initially empty list isolated from other attempts.</value>
    public List<string> Requests { get; } = [];
}

/// <summary>Registers independently selected primary, replica and default software endpoints.</summary>
public sealed class KeyedProfile : InjectionProfile
{
    /// <summary>Declares scoped endpoints with and without keys.</summary>
    /// <param name="services">Attempt-local dependency registrations.</param>
    public override void Configure(IServiceCollection services)
    {
        services.AddKeyedScoped<KeyedEndpoint>("primary", (_, _) => new("primary"));
        services.AddKeyedScoped<KeyedEndpoint>("replica", (_, _) => new("replica"));
        services.AddScoped<KeyedEndpoint>(_ => new("default"));
        services.AddKeyedScoped<KeyedEndpoint>(42, (_, _) => new("numeric"));
        services.AddKeyedScoped<KeyedEndpoint>("pipeline", (_, _) => new("validate"));
        services.AddKeyedScoped<KeyedEndpoint>("pipeline", (_, _) => new("save"));
        services.AddKeyedTransient<KeyedEndpoint>("transient", (_, _) => new("transient"));
        services.AddScoped<KeyedOrder>();
    }
}

/// <summary>Checks explicit key selection at fixture construction.</summary>
/// <param name="primary">Primary endpoint selected by its key.</param>
/// <param name="replica">Replica selected independently of the primary.</param>
/// <param name="fallback">Unkeyed default endpoint.</param>
[HarborInjectedFixture(typeof(KeyedProfile))]
public sealed class InjectionKeyedConstructorTests(
    [FromKeyedServices("primary")] KeyedEndpoint primary,
    [FromKeyedServices("replica")] KeyedEndpoint replica, KeyedEndpoint fallback)
{
    /// <summary>Verifies keyed registrations do not create ambiguity for the default service.</summary>
    [Test]
    public void SelectsConstructorKeys()
    {
        Assert.That(primary.Name, Is.EqualTo("primary"));
        Assert.That(replica.Name, Is.EqualTo("replica"));
        Assert.That(fallback.Name, Is.EqualTo("default"));
        Assert.That(primary, Is.Not.SameAs(replica));
        Assert.That(primary, Is.Not.SameAs(fallback));
    }
}

/// <summary>Mixes keyed service parameters with ordinary NUnit data.</summary>
/// <param name="primary">The constructor-selected scoped endpoint.</param>
[HarborInjectedFixture(typeof(KeyedProfile)), Parallelizable(ParallelScope.All)]
public sealed class InjectionKeyedMethodTests([FromKeyedServices("primary")] KeyedEndpoint primary)
{
    /// <summary>Checks explicit null selects only the ordinary unkeyed service.</summary>
    /// <param name="fallback">Default service requested using a null key.</param>
    [Test]
    public void NullKey([FromKeyedServices(null)] KeyedEndpoint fallback) => Assert.That(fallback.Name, Is.EqualTo("default"));

    /// <summary>Checks supported attribute keys are not restricted to strings.</summary>
    /// <param name="numeric">Service registered under an integer key.</param>
    [Test]
    public void NumericKey([FromKeyedServices(42)] KeyedEndpoint numeric) => Assert.That(numeric.Name, Is.EqualTo("numeric"));

    /// <summary>Checks method selection, data binding and fresh state across parallel cases.</summary>
    /// <param name="input">Ordinary case data.</param>
    /// <param name="same">The same keyed service supplied to the constructor.</param>
    /// <param name="expected">Expected data length.</param>
    [TestCase("read", 4), TestCase("write", 5)]
    public void RoutesData(string input, [FromKeyedServices("primary")] KeyedEndpoint same, int expected)
    {
        Assert.That(same, Is.SameAs(primary));
        Assert.That(same.Requests, Is.Empty);
        same.Requests.Add(input);
        Assert.That(primary.Requests.Single().Length, Is.EqualTo(expected));
        TestContext.Out.WriteLine("TestHarbor selects the keyed endpoint; NUnit supplies only the ordinary input and expected result.");
    }
}

/// <summary>Checks keyed collection filtering, order and element lifetimes.</summary>
/// <param name="pipeline">Two scoped elements under the pipeline key.</param>
/// <param name="transients">Transient elements resolved during construction.</param>
[HarborInjectedFixture(typeof(KeyedProfile)), Parallelizable(ParallelScope.All)]
public sealed class InjectionKeyedCollectionTests(
    [FromKeyedServices("pipeline")] IEnumerable<KeyedEndpoint> pipeline,
    [FromKeyedServices("transient")] IEnumerable<KeyedEndpoint> transients)
{
    /// <summary>Checks only matching keys are returned in registration order with shared scoped identity.</summary>
    /// <param name="same">The pipeline resolved again for the method.</param>
    [Test]
    public void OrderedPipeline([FromKeyedServices("pipeline")] IEnumerable<KeyedEndpoint> same)
    {
        var expected = pipeline.ToArray();
        var actual = same.ToArray();
        Assert.That(actual.Select(x => x.Name), Is.EqualTo(new[] { "validate", "save" }));
        Assert.That(actual[0], Is.SameAs(expected[0]));
        Assert.That(actual[1], Is.SameAs(expected[1]));
    }

    /// <summary>Checks a missing key gives an empty collection without falling back to default registrations.</summary>
    /// <param name="missing">No implementations are registered with this key.</param>
    [Test]
    public void EmptyKey([FromKeyedServices("absent")] IEnumerable<KeyedEndpoint> missing) => Assert.That(missing, Is.Empty);

    /// <summary>Checks keyed transient collection elements are recreated for each resolution.</summary>
    /// <param name="fresh">Transient elements requested at the method boundary.</param>
    [Test]
    public void FreshTransients([FromKeyedServices("transient")] IEnumerable<KeyedEndpoint> fresh) =>
        Assert.That(fresh.Single(), Is.Not.SameAs(transients.Single()));
}
