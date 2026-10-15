using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor.Testing;

namespace TestHarbor.Tests;

/// <summary>Verifies explicit ownership, shared scope closure, lifecycle evidence and profiles.</summary>
public sealed class InfrastructureTests
{
    /// <summary>Checks phase observations and preservation of body and observer failures.</summary>
    /// <returns>A task completing after both failure paths are checked.</returns>
    [Test]
    public async Task ObserversCannotSuppressFailures()
    {
        var observations = new List<LifecycleObservation>();
        var error = await Assert.ThrowsAsync<AggregateException>(() => TestSession.RunAsync([], (_, _) =>
            throw new InvalidOperationException("body"), options: new() { Observer = o =>
            {
                observations.Add(o);
                if (o.Phase == SessionPhase.BodyFinished) throw new IOException("observer");
            }}));
        Assert.That(error!.Flatten().InnerExceptions.Select(x => x.Message), Does.Contain("body").And.Contain("observer"));
        Assert.That(observations.Select(x => x.Phase), Is.EqualTo(new[] { SessionPhase.Starting, SessionPhase.Ready,
            SessionPhase.BodyStarting, SessionPhase.BodyFinished, SessionPhase.Cleaning, SessionPhase.Finished }));
        Assert.That(observations[^1].IsFinal, Is.True);
    }

    /// <summary>Checks every owned cleanup is attempted and concurrent disposal does not repeat work.</summary>
    /// <returns>A task completing after both disposal callers observe the shared failure.</returns>
    [Test]
    public async Task OwnedCleanupAttemptsAllInReverse()
    {
        var events = new List<int>();
        var owned = new OwnedResources();
        var first = owned.Add(new Disposable(() => events.Add(1)));
        owned.Add(new Disposable(() => { events.Add(2); throw new IOException("dispose"); }));
        Assert.Throws<ArgumentException>(() => owned.Add(first));
        Assert.Throws<ArgumentException>(() => new OwnedResources().Add(first));
        await Assert.ThrowsAsync<AggregateException>(async () => await owned.DisposeAsync());
        await Assert.ThrowsAsync<AggregateException>(async () => await owned.DisposeAsync());
        Assert.That(events, Is.EqualTo(new[] { 2, 1 }));
    }

    /// <summary>Checks shared singletons, isolated scoped objects and parent closure waiting for children.</summary>
    /// <returns>A task completing when shared and scoped services finish cleanup.</returns>
    [Test]
    public async Task SharedScopesDrainChildren()
    {
        var disposed = 0;
        var owner = new TestServiceScope(s => { s.AddSingleton(_ => new Disposable(() => disposed++)); s.AddScoped(_ => new object()); });
        var a = await TestSession.OpenAsync([], options: new() { ServiceScope = owner });
        var b = await TestSession.OpenAsync([], options: new() { ServiceScope = owner });
        Assert.That(a.Services.GetRequiredService<Disposable>(), Is.SameAs(b.Services.GetRequiredService<Disposable>()));
        Assert.That(a.Services.GetRequiredService<object>(), Is.Not.SameAs(b.Services.GetRequiredService<object>()));
        var closing = owner.DisposeAsync().AsTask();
        Assert.That(closing.IsCompleted, Is.False);
        await a.DisposeAsync();
        await b.DisposeAsync();
        await closing;
        Assert.That(disposed, Is.EqualTo(1));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => TestSession.OpenAsync([], options: new() { ServiceScope = owner }));
    }

    /// <summary>Checks temporary evidence retention after a body failure and deletion after success.</summary>
    /// <returns>A task completing after both policies are verified.</returns>
    [Test]
    public async Task TemporaryDirectoryRetention()
    {
        string? success = null;
        string? failed = null;
        await TestSession.RunAsync([], (s, _) => { success = s.CreateTemporaryDirectory().Path; return Task.CompletedTask; });
        await Assert.ThrowsAsync<IOException>(() => TestSession.RunAsync([], (s, _) =>
        { failed = s.CreateTemporaryDirectory().Path; throw new IOException("body"); }));
        Assert.That(Directory.Exists(success), Is.False);
        Assert.That(Directory.Exists(failed), Is.True);
        Directory.Delete(failed!, true);
    }

    /// <summary>Checks a third-party extension can use the public probe and that stop failure quarantines ownership.</summary>
    /// <returns>A task completing after extension evidence is checked.</returns>
    [Test]
    public async Task ExtensionProbeCapturesCleanupFailures()
    {
        var probe = new ExtensionProbe();
        var result = await probe.RunAsync(s => s.AddScoped<ITestExtension>(_ => new ProbeExtension(probe)));
        Assert.That(result.Events, Is.EqualTo(new[] { "start", "stop" }));
        Assert.That(result.Failure, Is.TypeOf<AggregateException>());
        Assert.That(result.LeaseReleased && result.Quarantined, Is.True);
    }

    /// <summary>Checks profile composition preserves order and duplicate rejection leaves it unchanged.</summary>
    [Test]
    public void ProfilesAreExplicitAndDeterministic()
    {
        var order = new List<int>();
        var common = new ExtensionProfile().Add("first", _ => order.Add(1));
        var combined = new ExtensionProfile().Include(common).Add("second", _ => order.Add(2));
        Assert.Throws<ArgumentException>(() => combined.Include(common));
        combined.Apply(new ServiceCollection());
        Assert.That(order, Is.EqualTo(new[] { 1, 2 }));
    }

    /// <summary>Checks recursive session disposal fails promptly instead of deadlocking its cleanup task.</summary>
    /// <returns>A task completing after the recursive disposer is rejected.</returns>
    [Test]
    public async Task RecursiveDisposalFailsWithoutDeadlock()
    {
        var session = await TestSession.OpenAsync([]);
        session.Owned.Add(new RecursiveDisposer(session));
        var error = await Assert.ThrowsAsync<AggregateException>(async () =>
            await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.That(error!.Flatten().InnerExceptions.Any(x => x.Message.Contains("Recursive session")), Is.True);
    }

    /// <summary>Checks named-owner recursive acquisition is diagnosed instead of entering the wait queue.</summary>
    /// <returns>A task completing after rejection and lease release.</returns>
    [Test]
    public async Task RecursiveAcquisitionIsDiagnosed()
    {
        var coordinator = new ResourceCoordinator();
        using var lease = await coordinator.AcquireAsync([new("same")], owner: "owner");
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.AcquireAsync([new("same")], owner: "owner"));
        Assert.That(coordinator.GetSnapshot().Single().Waiting, Is.Zero);
    }

    /// <summary>Attempts prohibited recursive session disposal to exercise the misuse guard.</summary>
    /// <param name="session">Session whose cleanup is already running.</param>
    private sealed class RecursiveDisposer(TestSession session) : IAsyncDisposable
    {
        /// <summary>Attempts to await the owning session's already-running cleanup.</summary>
        /// <returns>The recursive cleanup attempt, which must fail immediately.</returns>
        public ValueTask DisposeAsync() => session.DisposeAsync();
    }

    /// <summary>Adapts a callback to a disposable test resource.</summary>
    /// <param name="dispose">Callback executed at disposal.</param>
    private sealed class Disposable(Action dispose) : IDisposable
    {
        /// <summary>Executes the cleanup callback.</summary>
        public void Dispose() => dispose();
    }

    /// <summary>Records extension calls and injects a stop failure.</summary>
    /// <param name="probe">Recorder owned by the test.</param>
    private sealed class ProbeExtension(ExtensionProbe probe) : ITestExtension
    {
        /// <summary>Records successful initialization.</summary>
        /// <param name="context">Session context.</param>
        /// <param name="cancellationToken">Startup cancellation.</param>
        /// <returns>A completed startup task.</returns>
        public Task StartAsync(TestExtensionContext context, CancellationToken cancellationToken)
        { probe.Record("start"); return Task.CompletedTask; }
        /// <summary>Records cleanup before reporting failure.</summary>
        /// <param name="context">Session context.</param>
        /// <returns>A faulted cleanup task.</returns>
        public Task StopAsync(TestExtensionContext context)
        { probe.Record("stop"); return Task.FromException(new IOException("stop")); }
    }
}
