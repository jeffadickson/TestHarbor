using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor.NUnit;

namespace TestHarbor.Tests;

/// <summary>Verifies that public NUnit hooks retain resources and services through the derived lifecycle.</summary>
[RequiresResource("LifecycleProbe")]
[Parallelizable(ParallelScope.All)]
public sealed class LifecycleTests : HarborFixture
{
    /// <summary>Records each probe state at disposal to verify teardown precedes service cleanup.</summary>
    private static readonly ConcurrentDictionary<string, int> States = new();
    /// <summary>Counts active tests to detect overlap before resource ownership is released.</summary>
    private static int active;
    /// <summary>Registers per-test services used by this fixture and its lifecycle verification.</summary>
    /// <param name="services">Collection receiving per-test dependency registrations.</param>
    protected override void ConfigureServices(IServiceCollection services) => services.AddScoped(_ =>
        new Probe(TestContext.CurrentContext.Test.ID));

    /// <summary>Initializes isolated probe state and detects overlapping resource owners.</summary>
    [SetUp]
    public void DerivedSetup()
    {
        Assert.That(Interlocked.Increment(ref active), Is.EqualTo(1));
        Services.GetRequiredService<Probe>().State = 1;
    }

    /// <summary>Verifies each parameterized case sees isolated services throughout setup, body and teardown.</summary>
    /// <param name="number">Case identifier used to verify independent lifecycle state.</param>
    /// <returns>A task completing when the verification and all awaited cleanup have finished.</returns>
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

    /// <summary>Confirms test body state and marks teardown completion before disposal.</summary>
    [TearDown]
    public void DerivedTeardown()
    {
        Assert.That(Services.GetRequiredService<Probe>().State, Is.GreaterThan(10));
        Services.GetRequiredService<Probe>().State = 100;
    }

    /// <summary>Confirms every test probe reached teardown and released its active ownership count.</summary>
    [OneTimeTearDown]
    public static void VerifyCleanup()
    {
        Assert.That(States.Count, Is.EqualTo(4));
        Assert.That(States.Values, Is.All.EqualTo(100));
        Assert.That(active, Is.Zero);
    }

    /// <summary>Carries per-test state and records its final value when the DI scope disposes it.</summary>
    /// <param name="id">NUnit test identifier used to record disposal evidence.</param>
    private sealed class Probe(string id) : IAsyncDisposable
    {
        /// <summary>Gets or sets the lifecycle stage observed by this per-test probe.</summary>
        /// <value>The current numeric stage of the lifecycle probe.</value>
        public int State { get; set; }
        /// <summary>Records final teardown state and decrements the active-owner counter after yielding.</summary>
        /// <returns>A value task completing when cleanup finishes; cleanup failures propagate to the caller.</returns>
        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            States[id] = State;
            Interlocked.Decrement(ref active);
        }
    }
}
