using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace TestHarbor.Tests;

public sealed class ExtensionTests
{
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

    [Test]
    public async Task BodyFailureWithSuccessfulCleanupDoesNotQuarantine()
    {
        var coordinator = new ResourceCoordinator();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await TestSession.RunAsync([new("A")],
            (_, _) => throw new InvalidOperationException("body"), coordinator: coordinator));
        using var lease = await coordinator.AcquireAsync([new("A")]);
    }

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

    private sealed class Extension(string name, List<string> log, bool failStart = false, bool failStop = false) : ITestExtension
    {
        public Task StartAsync(TestExtensionContext context, CancellationToken cancellationToken)
        { log.Add("+" + name); if (failStart) throw new InvalidOperationException("start-" + name); return Task.CompletedTask; }
        public Task StopAsync(TestExtensionContext context)
        { log.Add("-" + name); if (failStop) throw new InvalidOperationException("stop-" + name); return Task.CompletedTask; }
    }
    private sealed class CallbackExtension(Func<TestExtensionContext, CancellationToken, Task> start,
        Func<TestExtensionContext, Task> stop) : ITestExtension
    {
        public Task StartAsync(TestExtensionContext context, CancellationToken cancellationToken) => start(context, cancellationToken);
        public Task StopAsync(TestExtensionContext context) => stop(context);
    }
    private sealed class Disposable(Action dispose) : IDisposable { public void Dispose() => dispose(); }
}
