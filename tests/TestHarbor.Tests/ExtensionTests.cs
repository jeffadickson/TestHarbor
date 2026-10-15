using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace TestHarbor.Tests;

/// <summary>Verifies extension ordering, rollback, cancellation and cleanup error propagation.</summary>
public sealed class ExtensionTests
{
    /// <summary>Verifies registration-order startup, reverse stops and subsequent service disposal.</summary>
    /// <returns>A task completing when the verification and all awaited cleanup have finished.</returns>
    [Test]
    public async Task ExtensionsStartInOrderStopInReverseThenDisposeServices()
    {
        List<string> log = [];
        var coordinator = new ResourceCoordinator();
        await TestSession.RunAsync([new("A")], (session, _) =>
        {
            session.Services.GetRequiredService<Disposable>();
            log.Add("body"); return Task.CompletedTask;
        }, services =>
        {
            services.AddScoped<ITestExtension>(_ => new Extension("A", log));
            services.AddScoped<ITestExtension>(_ => new Extension("B", log));
            services.AddScoped(_ => new Disposable(() => log.Add("dispose")));
        }, coordinator);
        Assert.That(log, Is.EqualTo(new[] { "+A", "+B", "body", "-B", "-A", "dispose" }));
    }

    /// <summary>Verifies partial startup rolls back every entered extension and preserves all stop errors.</summary>
    /// <returns>A task completing when the verification and all awaited cleanup have finished.</returns>
    [Test]
    public async Task PartialStartUnwindsIncludingFailedExtensionAndPreservesEveryError()
    {
        List<string> log = [];
        var coordinator = new ResourceCoordinator();
        var error = await Assert.ThrowsAsync<AggregateException>(async () =>
            await TestSession.OpenAsync([new("A")], services =>
            {
                services.AddScoped<ITestExtension>(_ => new Extension("A", log, failStop: true));
                services.AddScoped<ITestExtension>(_ => new Extension("B", log, failStart: true, failStop: true));
                services.AddScoped<ITestExtension>(_ => new Extension("C", log));
            }, coordinator));
        Assert.That(log, Is.EqualTo(new[] { "+A", "+B", "-B", "-A" }));
        Assert.That(error!.Flatten().InnerExceptions.Select(x => x.Message),
            Is.EquivalentTo(new[] { "start-B", "stop-B", "stop-A" }));
        Assert.That(coordinator.GetSnapshot().Single().QuarantineReason, Is.Not.Null);
    }

    /// <summary>Verifies startup cancellation does not suppress rollback or resource release.</summary>
    /// <returns>A task completing when the verification and all awaited cleanup have finished.</returns>
    [Test]
    public async Task CancelledStartStillStopsEnteredExtensionAndReleasesResource()
    {
        using var token = new CancellationTokenSource();
        var stopped = false;
        var coordinator = new ResourceCoordinator();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await TestSession.OpenAsync([new("A")], services => services.AddScoped<ITestExtension>(_ =>
                new CallbackExtension((_, ct) => { token.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; },
                    _ => { stopped = true; return Task.CompletedTask; })), coordinator, token.Token));
        Assert.That(stopped, Is.True);
        using var lease = await coordinator.AcquireAsync([new("A")]);
    }

    /// <summary>Verifies concurrent disposal joins one cleanup task and all callers observe its failure.</summary>
    /// <returns>A task completing when the verification and all awaited cleanup have finished.</returns>
    [Test]
    public async Task ConcurrentDisposersJoinSameWorkAndObserveSameFailure()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stops = 0;
        var session = await TestSession.OpenAsync([], services => services.AddScoped<ITestExtension>(_ =>
            new CallbackExtension((_, _) => Task.CompletedTask, async _ =>
            {
                Interlocked.Increment(ref stops); entered.SetResult(); await finish.Task;
                throw new InvalidOperationException("stop");
            })));
        var first = session.DisposeAsync().AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = session.DisposeAsync().AsTask();
        Assert.That(second, Is.SameAs(first));
        Assert.That(second.IsCompleted, Is.False);
        finish.SetResult();
        await Assert.ThrowsAsync<AggregateException>(async () => await first);
        await Assert.ThrowsAsync<AggregateException>(async () => await second);
        Assert.That(stops, Is.EqualTo(1));
    }

    /// <summary>Verifies stop failure still disposes services and quarantines before a queued waiter can acquire.</summary>
    /// <returns>A task completing when the verification and all awaited cleanup have finished.</returns>
    [Test]
    public async Task StopFailureStillDisposesServicesAndQuarantinesBeforeWaiterCanEnter()
    {
        var coordinator = new ResourceCoordinator();
        var disposed = false;
        var session = await TestSession.OpenAsync([new("A")], services =>
        {
            services.AddScoped<ITestExtension>(_ => new CallbackExtension((ctx, _) =>
            { ctx.Services.GetRequiredService<Disposable>(); return Task.CompletedTask; },
                _ => throw new InvalidOperationException("stop")));
            services.AddScoped(_ => new Disposable(() => disposed = true));
        }, coordinator);
        var waiter = coordinator.AcquireAsync([new("A")]);
        await Assert.ThrowsAsync<AggregateException>(async () => await session.DisposeAsync());
        await Assert.ThrowsAsync<ResourceQuarantinedException>(async () => await waiter);
        Assert.That(disposed, Is.True);
        Assert.That(coordinator.GetSnapshot().Single().Exclusive, Is.False);
    }

    /// <summary>Verifies ordinary body failure does not quarantine resources when cleanup succeeds.</summary>
    /// <returns>A task completing when the verification and all awaited cleanup have finished.</returns>
    [Test]
    public async Task BodyFailureWithSuccessfulCleanupDoesNotQuarantine()
    {
        var coordinator = new ResourceCoordinator();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await TestSession.RunAsync([new("A")],
            (_, _) => throw new InvalidOperationException("body"), coordinator: coordinator));
        using var lease = await coordinator.AcquireAsync([new("A")]);
    }

    /// <summary>Verifies opting out of quarantine does not hide cleanup errors.</summary>
    /// <returns>A task completing when the verification and all awaited cleanup have finished.</returns>
    [Test]
    public async Task ExplicitNonQuarantiningPolicyStillReportsCleanupFailure()
    {
        var coordinator = new ResourceCoordinator();
        var session = await TestSession.OpenAsync([new("A")], services =>
            services.AddScoped<ITestExtension>(_ => new CallbackExtension((_, _) => Task.CompletedTask,
                _ => throw new InvalidOperationException("stop"))), coordinator,
            options: new TestSessionOptions { QuarantineOnCleanupFailure = false });
        await Assert.ThrowsAsync<AggregateException>(async () => await session.DisposeAsync());
        using var lease = await coordinator.AcquireAsync([new("A")]);
    }

    /// <summary>Logs extension lifecycle transitions and optionally fails either phase.</summary>
    /// <param name="name">Extension label written to the lifecycle trace.</param>
    /// <param name="log">Ordered trace collecting start, body and stop events.</param>
    /// <param name="failStart">Whether startup should throw after recording entry.</param>
    /// <param name="failStop">Whether cleanup should throw after recording entry.</param>
    private sealed class Extension(string name, List<string> log, bool failStart = false, bool failStop = false) : ITestExtension
    {
        /// <summary>Records startup and optionally throws to simulate partial initialization.</summary>
        /// <param name="context">Per-test services and diagnostic owner identity, still valid during extension cleanup.</param>
        /// <param name="cancellationToken">Cooperative startup cancellation token.</param>
        /// <returns>A task completing when startup finishes, or faulting if initialization fails.</returns>
        public Task StartAsync(TestExtensionContext context, CancellationToken cancellationToken)
        { log.Add("+" + name); if (failStart) throw new InvalidOperationException("start-" + name); return Task.CompletedTask; }
        /// <summary>Records cleanup and optionally throws while testing reverse unwinding.</summary>
        /// <param name="context">Per-test services and diagnostic owner identity, still valid during extension cleanup.</param>
        /// <returns>A task completing when cleanup finishes, or faulting if cleanup fails.</returns>
        public Task StopAsync(TestExtensionContext context)
        { log.Add("-" + name); if (failStop) throw new InvalidOperationException("stop-" + name); return Task.CompletedTask; }
    }
    /// <summary>Adapts callbacks into a test extension for precise async lifecycle control.</summary>
    /// <param name="start">Async startup callback supplied by the test.</param>
    /// <param name="stop">Async cleanup callback supplied by the test.</param>
    private sealed class CallbackExtension(Func<TestExtensionContext, CancellationToken, Task> start,
        Func<TestExtensionContext, Task> stop) : ITestExtension
    {
        /// <summary>Invokes the supplied startup callback.</summary>
        /// <param name="context">Per-test services and diagnostic owner identity, still valid during extension cleanup.</param>
        /// <param name="cancellationToken">Cooperative startup cancellation token.</param>
        /// <returns>A task completing when startup finishes, or faulting if initialization fails.</returns>
        public Task StartAsync(TestExtensionContext context, CancellationToken cancellationToken) => start(context, cancellationToken);
        /// <summary>Invokes the supplied cleanup callback.</summary>
        /// <param name="context">Per-test services and diagnostic owner identity, still valid during extension cleanup.</param>
        /// <returns>A task completing when cleanup finishes, or faulting if cleanup fails.</returns>
        public Task StopAsync(TestExtensionContext context) => stop(context);
    }
    /// <summary>Invokes an injected disposal action to verify service cleanup independently of extensions.</summary>
    /// <param name="dispose">Service disposal action supplied by the test.</param>
    private sealed class Disposable(Action dispose) : IDisposable {
        /// <summary>Invokes the injected synchronous disposal callback once per DI-owned instance.</summary>
        public void Dispose() => dispose(); }
}
