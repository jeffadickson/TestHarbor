using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor.NUnit;
using TestHarbor.NUnit.Injection;

namespace TestHarbor.Example.Injection;

/// <summary>Exercises ordinary NUnit case discovery with runtime constructor and method services.</summary>
[HarborInjectedFixture(typeof(OrderProfile))]
[RequiresResource("Injected/Declared", ResourceAccess.SharedRead)]
[Parallelizable(ParallelScope.All)]
public sealed class InjectedOrderTests : IAsyncDisposable
{
    /// <summary>Retains the constructor-injected attempt-local store.</summary>
    private readonly OrderStore orders;

    /// <summary>Receives a live service after the attempt has acquired its resource and started extensions.</summary>
    /// <param name="orders">The same scoped store later supplied to method parameters.</param>
    public InjectedOrderTests(OrderStore orders)
    {
        this.orders = orders;
        orders.Record("constructor");
        Assert.That(orders.Started, Is.True);
        Assert.That(ResourceCoordinator.Shared.GetSnapshot().Any(s => s.Name == "Injected/Declared" && s.Readers > 0), Is.True);
    }

    /// <summary>Checks the constructor dependency remains available in ordinary NUnit setup.</summary>
    [SetUp]
    public void Setup() { orders.Record("setup"); Assert.That(orders.Disposed, Is.False); }

    /// <summary>Mixes service parameters on either side of ordinary NUnit case data.</summary>
    /// <param name="sameStore">Store resolved from the constructor's scope.</param>
    /// <param name="product">Ordinary NUnit case data.</param>
    /// <param name="clock">Another scoped dependency.</param>
    /// <returns>A task completing after an asynchronous application operation.</returns>
    [TestCase("book"), TestCase("pencil"), RequiresResource("Injected/Method")]
    public async Task SavesOrder([FromServices] OrderStore sameStore, string product, [FromServices] OrderClock clock)
    {
        await Task.Yield();
        Assert.That(ResourceCoordinator.Shared.GetSnapshot().Any(s => s.Name == "Injected/Method" && s.Exclusive), Is.True);
        Assert.That(sameStore, Is.SameAs(orders));
        Assert.That(clock, Is.SameAs(orders.Clock));
        Assert.That(orders.Products, Is.Empty);
        orders.Products.Add(product);
        orders.Record("body");
        TestContext.Out.WriteLine("[Injection] NUnit bound product data; TestHarbor supplied the same scoped store to the constructor and method after acquiring ownership.");
    }

    /// <summary>Gets discovery-only nullable data without resolving services.</summary>
    /// <value>Two stable ordinary NUnit cases.</value>
    public static IEnumerable<TestCaseData> Products =>
        [new TestCaseData(new object?[] { null }).SetName("NullProduct"), new TestCaseData("").SetName("EmptyProduct")];

    /// <summary>Preserves source case names and nullable data with a trailing injected service.</summary>
    /// <param name="product">Null or empty application input.</param>
    /// <param name="sameStore">The constructor's scoped repository.</param>
    [TestCaseSource(nameof(Products))]
    public void SourceData(string? product, [FromServices] OrderStore sameStore)
    { Assert.That(product, Is.Null.Or.Empty); Assert.That(sameStore, Is.SameAs(orders)); orders.Record("body"); }

    /// <summary>Lets NUnit await and compare the original asynchronous return value.</summary>
    /// <param name="value">Ordinary case input.</param>
    /// <param name="sameStore">The constructor's scoped repository.</param>
    /// <returns>The input value after an asynchronous continuation.</returns>
    [TestCase(7, ExpectedResult = 7)]
    public async Task<int> ReturnedValue(int value, [FromServices] OrderStore sameStore)
    { await Task.Yield(); Assert.That(sameStore, Is.SameAs(orders)); orders.Record("body"); return value; }

    /// <summary>Checks every repeat starts with a newly constructed fixture and empty store.</summary>
    [Test, Repeat(2)]
    public void Repeated() { Assert.That(orders.Products, Is.Empty); orders.Products.Add("repeat"); orders.Record("body"); }

    /// <summary>Fails the first attempt and proves retry starts with an empty scoped dependency.</summary>
    [Test, Retry(2)]
    public void Retried()
    {
        Assert.That(orders.Products, Is.Empty);
        orders.Products.Add("retry");
        orders.Record("body");
        Assert.That(TestContext.CurrentContext.CurrentRepeatCount, Is.EqualTo(1), "Deliberate first-attempt failure.");
    }

    /// <summary>Checks ordinary NUnit teardown runs before service disposal.</summary>
    [TearDown]
    public void Teardown() { orders.Record("teardown"); Assert.That(orders.Disposed, Is.False); }

    /// <summary>Awaits fixture cleanup while injected dependencies and resource ownership remain live.</summary>
    /// <returns>An awaitable completing before extension and service cleanup.</returns>
    public async ValueTask DisposeAsync() { await Task.Yield(); orders.Record("fixture-dispose"); Assert.That(orders.Disposed, Is.False); }
}

/// <summary>Defines runtime-only services and a resource unique to each independently selected test case.</summary>
public class OrderProfile : InjectionProfile
{
    /// <summary>Gets a stable per-case resource name shared across retry attempts.</summary>
    /// <value>The resource protecting the case's environment.</value>
    public override IEnumerable<ResourceRequest> Resources => [new("Injected/" + TestContext.CurrentContext.Test.FullName)];

    /// <summary>Records released ownership and quarantine after each session, independently of test assertions.</summary>
    /// <value>An observer that emits final coordinator state when evidence capture is enabled.</value>
    public override TestSessionOptions Options => new()
    {
        Observer = observation =>
        {
            if (observation.Phase != SessionPhase.Finished) return;
            var test = TestContext.CurrentContext.Test;
            var state = ResourceCoordinator.Shared.GetSnapshot().SingleOrDefault(s => s.Name == "Injected/" + test.FullName);
            var output = Environment.GetEnvironmentVariable("TESTHARBOR_INJECTION_OUTPUT");
            if (output is not null)
                File.WriteAllText(Path.Combine(output, "finished-" + Guid.NewGuid().ToString("N") + ".json"),
                    JsonSerializer.Serialize(new { test = test.FullName, method = test.MethodName,
                        held = state?.Exclusive ?? false, waiting = state?.Waiting ?? 0,
                        quarantine = state?.QuarantineReason, attempt = TestContext.CurrentContext.CurrentRepeatCount }));
        }
    };

    /// <summary>Registers one clock, store and startup/cleanup extension per attempt.</summary>
    /// <param name="services">Attempt-local registrations.</param>
    public override void Configure(IServiceCollection services)
    {
        services.AddScoped<OrderClock>();
        services.AddScoped<OrderStore>();
        services.AddScoped<ITestExtension, OrderExtension>();
    }
}

/// <summary>Represents a small application dependency whose identity is shared within one attempt.</summary>
public sealed class OrderClock;

/// <summary>Models a scoped application repository and records independent lifecycle evidence.</summary>
/// <param name="clock">The attempt-local clock supplied by DI.</param>
public sealed class OrderStore(OrderClock clock) : IAsyncDisposable
{
    /// <summary>Identifies one concrete service instance across all lifecycle events.</summary>
    private readonly string identity = Guid.NewGuid().ToString("N");
    /// <summary>Gets the clock dependency retained by this repository.</summary>
    /// <value>The scoped clock instance.</value>
    public OrderClock Clock { get; } = clock;
    /// <summary>Gets the records local to this attempt.</summary>
    /// <value>Mutable attempt-local product data.</value>
    public List<string> Products { get; } = [];
    /// <summary>Gets or sets whether extension startup has completed.</summary>
    /// <value>True after startup.</value>
    public bool Started { get; set; }
    /// <summary>Gets whether async disposal completed.</summary>
    /// <value>True only after disposal.</value>
    public bool Disposed { get; private set; }

    /// <summary>Writes lifecycle evidence and verifies the named lease is still held.</summary>
    /// <param name="phase">The lifecycle phase being recorded.</param>
    public void Record(string phase)
    {
        var test = TestContext.CurrentContext.Test;
        Assert.That(ResourceCoordinator.Shared.GetSnapshot().Any(s => s.Name == "Injected/" + test.FullName && s.Exclusive), Is.True);
        var output = Environment.GetEnvironmentVariable("TESTHARBOR_INJECTION_OUTPUT");
        if (output is not null)
            File.AppendAllLines(Path.Combine(output, identity + ".jsonl"), [JsonSerializer.Serialize(new
            { phase, identity, test = test.FullName, method = test.MethodName, attempt = TestContext.CurrentContext.CurrentRepeatCount })]);
    }

    /// <summary>Records asynchronous service cleanup while the session still holds its lease.</summary>
    /// <returns>An awaitable completing once the service is disposed.</returns>
    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        Assert.That(Disposed, Is.False, "Service must be disposed exactly once.");
        Record("service-dispose");
        Disposed = true;
        if (TestContext.CurrentContext.Test.MethodName == "ServiceDisposeFails") throw new IOException("service-dispose-marker");
    }
}

/// <summary>Provides lifecycle evidence around construction and NUnit-owned fixture disposal.</summary>
/// <param name="orders">The same scoped repository injected into the test.</param>
public sealed class OrderExtension(OrderStore orders) : ITestExtension
{
    /// <summary>Marks the service ready before fixture construction.</summary>
    /// <param name="context">Attempt identity and services.</param>
    /// <param name="cancellationToken">Startup cancellation.</param>
    /// <returns>A completed startup task.</returns>
    public Task StartAsync(TestExtensionContext context, CancellationToken cancellationToken)
    { orders.Record("start"); orders.Started = true; return Task.CompletedTask; }

    /// <summary>Records extension cleanup after NUnit fixture disposal.</summary>
    /// <param name="context">Attempt identity and services.</param>
    /// <returns>A completed cleanup task.</returns>
    public Task StopAsync(TestExtensionContext context) { orders.Record("stop"); return Task.CompletedTask; }
}
