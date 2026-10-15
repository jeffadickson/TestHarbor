using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor.NUnit;

namespace TestHarbor.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class LifecycleTests : HarborFixture
{
    private static readonly ConcurrentDictionary<string, int> States = new();
    private static int active;
    protected override IEnumerable<ResourceRequest> Resources => [new("LifecycleProbe")];
    protected override void ConfigureServices(IServiceCollection services) => services.AddScoped(_ =>
        new Probe(TestContext.CurrentContext.Test.ID));

    [SetUp]
    public void DerivedSetup()
    {
        Assert.That(Interlocked.Increment(ref active), Is.EqualTo(1));
        Services.GetRequiredService<Probe>().State = 1;
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public async Task SessionSpansDerivedLifecycle(int number)
    {
        Assert.That(Services.GetRequiredService<Probe>().State, Is.EqualTo(1));
        await Task.Yield();
        Services.GetRequiredService<Probe>().State = number + 10;
    }

    [TearDown]
    public void DerivedTeardown()
    {
        Assert.That(Services.GetRequiredService<Probe>().State, Is.GreaterThan(10));
        Services.GetRequiredService<Probe>().State = 100;
    }

    [OneTimeTearDown]
    public static void VerifyCleanup()
    {
        Assert.That(States.Count, Is.EqualTo(4));
        Assert.That(States.Values, Is.All.EqualTo(100));
        Assert.That(active, Is.Zero);
    }

    private sealed class Probe(string id) : IAsyncDisposable
    {
        public int State { get; set; }
        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            States[id] = State;
            Interlocked.Decrement(ref active);
        }
    }
}
