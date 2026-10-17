using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor.NUnit.Injection;

namespace TestHarbor.Example.Injection;

/// <summary>Checks NUnit generic case construction retains runtime injection.</summary>
/// <param name="endpoint">Constructor service shared with generic methods.</param>
[HarborInjectedFixture(typeof(GenericMethodProfile)), Parallelizable(ParallelScope.All)]
public sealed class InjectionGenericMethodTests(KeyedEndpoint endpoint)
{
    /// <summary>Infers method types from ordinary case data while excluding services.</summary>
    /// <typeparam name="T">Type inferred by NUnit from the case.</typeparam>
    /// <param name="same">Scoped service before generic data.</param>
    /// <param name="value">Ordinary integer or string case data.</param>
    [TestCase(7), TestCase("book")]
    public void Inferred<T>([FromServices] KeyedEndpoint same, T value)
    {
        Assert.That(same, Is.SameAs(endpoint));
        Assert.That(value, Is.EqualTo(7).Or.EqualTo("book"));
        Assert.That(typeof(T), Is.EqualTo(value!.GetType()));
    }
    /// <summary>Honors explicit generic type arguments instead of inferring the literal's type.</summary>
    /// <typeparam name="T">Explicitly requested numeric type.</typeparam>
    /// <param name="value">Value converted by NUnit to the selected type.</param>
    /// <param name="same">Scoped service excluded from type inference.</param>
    [TestCase(7, TypeArgs = new[] { typeof(long) })]
    public void ExplicitType<T>(T value, [FromServices] KeyedEndpoint same)
    { Assert.That(typeof(T), Is.EqualTo(typeof(long))); Assert.That(value, Is.EqualTo(7L)); Assert.That(same, Is.SameAs(endpoint)); }

    /// <summary>Closes an open generic registration using the type selected from ordinary case data.</summary>
    /// <typeparam name="T">Application value type inferred by NUnit.</typeparam>
    /// <param name="value">Ordinary case value.</param>
    /// <param name="repository">Repository of the inferred type.</param>
    /// <param name="same">Repeated resolution of the same closed scoped service.</param>
    [TestCase(7), TestCase("book")]
    public void ClosedService<T>(T value, [FromServices] GenericRepository<T> repository, [FromServices] GenericRepository<T> same)
    { Assert.That(repository, Is.SameAs(same)); Assert.That(repository.Values, Is.Empty); repository.Values.Add(value); Assert.That(same.Values, Is.EqualTo(new[] { value }));
        TestContext.Out.WriteLine("NUnit selects the generic case type; TestHarbor resolves and cleans up its typed repository. One reusable contract tests integer and string repositories without per-type fixture setup.");
    }

    /// <summary>Closes a keyed generic registration independently of the default registration.</summary>
    /// <typeparam name="T">Case-inferred application value type.</typeparam>
    /// <param name="archive">Key-selected repository.</param>
    /// <param name="value">Ordinary application value.</param>
    /// <param name="ordinary">Unkeyed repository of the same type.</param>
    [TestCase(7), TestCase("book")]
    public void KeyedService<T>([FromKeyedServices("archive")] GenericRepository<T> archive, T value, [FromServices] GenericRepository<T> ordinary)
    { Assert.That(archive, Is.Not.SameAs(ordinary)); archive.Values.Add(value); Assert.That(ordinary.Values, Is.Empty); }

    /// <summary>Resolves a collection after generic case closure and preserves scoped element identity.</summary>
    /// <typeparam name="T">Case-selected repository value type.</typeparam>
    /// <param name="value">Ordinary case data.</param>
    /// <param name="all">Registered repositories of the closed type.</param>
    /// <param name="single">The same repository resolved singularly.</param>
    [TestCase(7), TestCase("book")]
    public void Collection<T>(T value, [FromServices] IEnumerable<GenericRepository<T>> all, [FromServices] GenericRepository<T> single)
    { Assert.That(all.Single(), Is.SameAs(single)); single.Values.Add(value); Assert.That(all.Single().Values.Single(), Is.EqualTo(value)); }

    /// <summary>Infers two independent type arguments with services between ordinary parameters.</summary>
    /// <typeparam name="TKey">Application identifier type.</typeparam>
    /// <typeparam name="TValue">Application value type.</typeparam>
    /// <param name="key">Identifier data.</param>
    /// <param name="keys">Identifier repository.</param>
    /// <param name="value">Value data.</param>
    /// <param name="values">Value repository.</param>
    [TestCase(7, "book"), TestCase("id", 9)]
    public void TwoTypes<TKey, TValue>(TKey key, [FromServices] GenericRepository<TKey> keys, TValue value, [FromServices] GenericRepository<TValue> values)
    { Assert.That(keys, Is.Not.SameAs(values)); keys.Values.Add(key); values.Values.Add(value); Assert.That(keys.Values.Single(), Is.EqualTo(key)); Assert.That(values.Values.Single(), Is.EqualTo(value)); }

    /// <summary>Honors valid CLR generic constraints alongside service binding.</summary>
    /// <typeparam name="T">A value type accepted by the contract.</typeparam>
    /// <param name="value">Ordinary constrained data.</param>
    /// <param name="repository">Repository closed with the constrained type.</param>
    [TestCase(7)]
    public void Constrained<T>(T value, [FromServices] GenericRepository<T> repository) where T : struct
    { repository.Values.Add(value); Assert.That(repository.Values.Single(), Is.EqualTo(value)); }

    /// <summary>Closes a service-only generic test using explicit type arguments.</summary>
    /// <typeparam name="T">Repository value type selected explicitly.</typeparam>
    /// <param name="repository">Service whose type cannot be inferred from case data.</param>
    [TestCase(TypeArgs = new[] { typeof(int) }), TestCase(TypeArgs = new[] { typeof(string) })]
    public void ServiceOnly<T>([FromServices] GenericRepository<T> repository)
    { Assert.That(repository.Values, Is.Empty); Assert.That(typeof(T), Is.EqualTo(typeof(int)).Or.EqualTo(typeof(string))); }

    /// <summary>Awaits a closed generic Task result before NUnit compares the expected value.</summary>
    /// <typeparam name="T">Case result type inferred by NUnit.</typeparam>
    /// <param name="value">Expected application result.</param>
    /// <param name="repository">Repository holding that result.</param>
    /// <returns>The value after asynchronous work completes.</returns>
    [TestCase(7, ExpectedResult = 7), TestCase("book", ExpectedResult = "book")]
    public async Task<T> TaskResult<T>(T value, [FromServices] GenericRepository<T> repository)
    { await Task.Yield(); repository.Values.Add(value); return repository.Values.Single(); }

    /// <summary>Awaits a closed generic ValueTask with a keyed service.</summary>
    /// <typeparam name="T">Case result type inferred by NUnit.</typeparam>
    /// <param name="repository">Keyed repository selected after generic closure.</param>
    /// <param name="value">Expected application result.</param>
    /// <returns>The value after an asynchronous continuation.</returns>
    [TestCase(7, ExpectedResult = 7), TestCase("book", ExpectedResult = "book")]
    public async ValueTask<T> ValueTaskResult<T>([FromKeyedServices("archive")] GenericRepository<T> repository, T value)
    { await Task.Yield(); repository.Values.Add(value); return repository.Values.Single(); }

    /// <summary>Preserves generic params inference and explicit empty-array construction.</summary>
    /// <typeparam name="T">Element type selected by NUnit.</typeparam>
    /// <param name="same">Service excluded from array preparation.</param>
    /// <param name="values">Packed ordinary values.</param>
    [TestCase(1, 2, 3), TestCase("book", "pen"), TestCase(TypeArgs = new[] { typeof(int) })]
    public void Params<T>([FromServices] KeyedEndpoint same, params T[] values)
    { Assert.That(same, Is.SameAs(endpoint)); Assert.That(values.Length, Is.AnyOf(0, 2, 3));
        if (values.Length == 3) Assert.That(values, Is.EqualTo(new[] { 1, 2, 3 }));
        if (values.Length == 2) Assert.That(values, Is.EqualTo(new[] { "book", "pen" })); Assert.That(values.GetType().GetElementType(), Is.EqualTo(typeof(T))); }

    /// <summary>Uses generic default values when an explicitly typed case omits ordinary data.</summary>
    /// <typeparam name="T">Explicitly selected optional data type.</typeparam>
    /// <param name="same">Required scoped service.</param>
    /// <param name="value">Omitted generic default supplied by NUnit.</param>
    [TestCase(TypeArgs = new[] { typeof(int) }), TestCase(TypeArgs = new[] { typeof(string) })]
    public void Optional<T>([FromServices] KeyedEndpoint same, T value = default!)
    { Assert.That(value, Is.EqualTo(default(T))); Assert.That(same, Is.SameAs(endpoint)); }

    /// <summary>Gets explicitly typed source rows without constructing any service.</summary>
    /// <value>One long-valued case whose literal is an integer.</value>
    public static IEnumerable<TestCaseData> TypedRows => [new TestCaseData(7) { TypeArgs = [typeof(long)] }];

    /// <summary>Retains source-provided type arguments through injection adaptation.</summary>
    /// <typeparam name="T">Explicit source-selected type.</typeparam>
    /// <param name="value">Data converted by NUnit to the selected type.</param>
    /// <param name="repository">Repository of that selected type.</param>
    [TestCaseSource(nameof(TypedRows))]
    public void TypedSource<T>(T value, [FromServices] GenericRepository<T> repository)
    { Assert.That(typeof(T), Is.EqualTo(typeof(long))); repository.Values.Add(value); Assert.That(repository.Values.Single(), Is.EqualTo(7L)); }

    /// <summary>Rebuilds the closed generic dependency for NUnit's second retry attempt.</summary>
    /// <typeparam name="T">Case-selected repository value type.</typeparam>
    /// <param name="value">Value written once per attempt.</param>
    /// <param name="repository">Fresh repository for this attempt.</param>
    [TestCase(7), TestCase("book"), Retry(2)]
    public void Retried<T>(T value, [FromServices] GenericRepository<T> repository)
    {
        Assert.That(repository.Values, Is.Empty);
        repository.Values.Add(value);
        Assert.That(TestContext.CurrentContext.CurrentRepeatCount, Is.EqualTo(1), "Deliberate first-attempt failure");
    }

    /// <summary>Creates and disposes a fresh generic service on every repeat.</summary>
    /// <typeparam name="T">Case-selected repository value type.</typeparam>
    /// <param name="value">Value written by the current attempt.</param>
    /// <param name="repository">Attempt-local generic service.</param>
    [TestCase(7), TestCase("book"), Repeat(3)]
    public void Repeated<T>(T value, [FromServices] GenericRepository<T> repository)
    { Assert.That(repository.Values, Is.Empty); repository.Values.Add(value); }

    /// <summary>Gets named generic source cases with independently filterable metadata.</summary>
    /// <value>Two named cases whose runtime services do not participate in identity.</value>
    public static IEnumerable<TestCaseData> NamedRows =>
    [new TestCaseData(7).SetName("NamedGenericInt").SetCategory("GenericReplay"),
     new TestCaseData("book").SetName("NamedGenericString").SetCategory("GenericReplay")];

    /// <summary>Preserves named generic source cases while resolving the matching repository.</summary>
    /// <typeparam name="T">Inferred source data type.</typeparam>
    /// <param name="value">Source-provided application value.</param>
    /// <param name="repository">Corresponding runtime-only dependency.</param>
    [TestCaseSource(nameof(NamedRows))]
    public void NamedGeneric<T>(T value, [FromServices] GenericRepository<T> repository)
    { repository.Values.Add(value); Assert.That(repository.Values.Single(), Is.EqualTo(value)); }

}

/// <summary>Registers services used by generic software contract tests.</summary>
public class GenericMethodProfile : InjectionProfile
{
    /// <summary>Registers an endpoint and an open generic repository.</summary>
    /// <param name="services">Per-attempt registration collection.</param>
    public override void Configure(IServiceCollection services)
    {
        services.AddScoped<KeyedEndpoint>(_ => new("default"));
        services.AddScoped(typeof(GenericRepository<>));
        services.AddKeyedScoped(typeof(GenericRepository<>), "archive", typeof(GenericRepository<>));
    }
}

/// <summary>Stores values of a test-selected application type within one attempt.</summary>
/// <typeparam name="T">Application value type.</typeparam>
public sealed class GenericRepository<T> : IDisposable
{
    /// <summary>Identifies this service instance across retries and repeated cases.</summary>
    private readonly Guid instance = Guid.NewGuid();
    /// <summary>Detects duplicate disposal of the same service instance.</summary>
    private bool disposed;

    /// <summary>Records disposal independently of the assertions inside generic test methods.</summary>
    public void Dispose()
    {
        if (disposed) throw new InvalidOperationException("Generic repository disposed twice");
        disposed = true;
        var output = Environment.GetEnvironmentVariable("TESTHARBOR_GENERIC_OUTPUT");
        if (output is null) return;
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, instance + ".json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            instance, type = typeof(T).FullName, count = Values.Count,
            method = TestContext.CurrentContext.Test.MethodName,
            name = TestContext.CurrentContext.Test.FullName,
            attempt = TestContext.CurrentContext.CurrentRepeatCount
        }));
    }

    /// <summary>Gets values written by the current attempt.</summary>
    /// <value>An initially empty attempt-local list.</value>
    public List<T> Values { get; } = [];
}
