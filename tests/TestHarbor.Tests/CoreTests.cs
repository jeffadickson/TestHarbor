using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace TestHarbor.Tests;

[TestFixture]
public class CoreTests
{
    [Test]
    public async Task ReadersOverlapAndWriterWaitsForAllReaders()
    {
        var c = new ResourceCoordinator();
        using var first = await c.AcquireAsync([new("A", ResourceAccess.SharedRead)]);
        using var second = await c.AcquireAsync([new("A", ResourceAccess.SharedRead)]);
        var writer = c.AcquireAsync([new("A")]);
        Assert.That(writer.IsCompleted, Is.False);
        first.Dispose();
        Assert.That(writer.IsCompleted, Is.False);
        second.Dispose();
        using var acquired = await writer.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task WriterBlocksReadersButUnrelatedResourcesCanOverlap()
    {
        var c = new ResourceCoordinator();
        using var writer = await c.AcquireAsync([new("A")]);
        using var other = await c.AcquireAsync([new("B")]);
        var reader = c.AcquireAsync([new("A", ResourceAccess.SharedRead)]);
        Assert.That(reader.IsCompleted, Is.False);
        writer.Dispose();
        using var acquired = await reader.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task RequestsAreAtomicAndCancellationRemovesQueueHead()
    {
        var c = new ResourceCoordinator();
        using var a = await c.AcquireAsync([new("A")]);
        using var cancellation = new CancellationTokenSource();
        var both = c.AcquireAsync([new("B"), new("A")], cancellation.Token);
        var b = c.AcquireAsync([new("B")]);
        Assert.That(b.IsCompleted, Is.False, "FIFO queue deliberately does not bypass its head.");
        cancellation.Cancel();
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await both);
        using var acquired = await b.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task DuplicateNamesMergeToExclusiveAndReleaseIsIdempotent()
    {
        var c = new ResourceCoordinator();
        var lease = await c.AcquireAsync([new("A", ResourceAccess.SharedRead), new("A")]);
        var next = c.AcquireAsync([new("A", ResourceAccess.SharedRead)]);
        Assert.That(next.IsCompleted, Is.False);
        lease.Dispose(); lease.Dispose();
        using var acquired = await next.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task CancelledRequestDoesNotAcquireResources()
    {
        var c = new ResourceCoordinator();
        using var token = new CancellationTokenSource(); token.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await c.AcquireAsync([new("A")], token.Token));
        using var acquired = await c.AcquireAsync([new("A")]).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task BadNamesAndModesAreRejected()
    {
        var c = new ResourceCoordinator();
        await Assert.ThrowsAsync<ArgumentException>(async () => await c.AcquireAsync([new(" ")]));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await c.AcquireAsync([new("A", (ResourceAccess)99)]));
    }

    [Test]
    public async Task ConcurrentWritersNeverOverlap()
    {
        var c = new ResourceCoordinator();
        var active = 0;
        await Task.WhenAll(Enumerable.Range(0, 100).Select(async i =>
        {
            using var lease = await c.AcquireAsync(i % 2 == 0
                ? [new("A"), new("B")] : [new("B"), new("A")]);
            Assert.That(Interlocked.Increment(ref active), Is.EqualTo(1));
            await Task.Yield();
            Assert.That(Interlocked.Decrement(ref active), Is.Zero);
        })).WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task CancellationGrantRacesDoNotLeakResources()
    {
        var c = new ResourceCoordinator();
        for (var i = 0; i < 100; i++)
        {
            var held = await c.AcquireAsync([new("A")]);
            using var token = new CancellationTokenSource();
            var pending = c.AcquireAsync([new("A")], token.Token);
            await Task.WhenAll(Task.Run(held.Dispose), Task.Run(token.Cancel));
            try { (await pending).Dispose(); } catch (OperationCanceledException) { }
            using var next = await c.AcquireAsync([new("A")]).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public async Task ServicesAreScopedAndAsyncCleanupHoldsTheLease()
    {
        var c = new ResourceCoordinator();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = await TestSession.OpenAsync([new("A")], services =>
            services.AddScoped(_ => new Cleanup(async () => { entered.SetResult(); await finish.Task; })), c);
        Assert.That(session.Services.GetRequiredService<Cleanup>(),
            Is.SameAs(session.Services.GetRequiredService<Cleanup>()));
        var disposing = session.DisposeAsync().AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var next = c.AcquireAsync([new("A")]);
        Assert.That(next.IsCompleted, Is.False);
        finish.SetResult();
        await disposing;
        using var acquired = await next.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Throws<ObjectDisposedException>(() => _ = session.Services);
        await session.DisposeAsync();
    }

    [Test]
    public async Task ConfigurationFailureReleasesTheLease()
    {
        var c = new ResourceCoordinator();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await TestSession.OpenAsync([new("A")], _ => throw new InvalidOperationException("setup"), c));
        using var acquired = await c.AcquireAsync([new("A")]).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task BodyAndCleanupFailuresAreBothPreservedAndLeaseIsReleased()
    {
        var c = new ResourceCoordinator();
        var error = await Assert.ThrowsAsync<AggregateException>(async () => await TestSession.RunAsync(
            [new("A")], (session, _) =>
            {
                session.Services.GetRequiredService<Cleanup>();
                throw new InvalidOperationException("body");
            }, services => services.AddScoped(_ => new Cleanup(() => throw new ApplicationException("cleanup"))), c));
        Assert.That(error!.Flatten().InnerExceptions.Select(x => x.Message), Is.EquivalentTo(new[] { "body", "cleanup" }));
        await Assert.ThrowsAsync<ResourceQuarantinedException>(async () => await c.AcquireAsync([new("A")]));
        c.ResetQuarantine("A", "Test fixture recovered simulated hardware.");
        using var acquired = await c.AcquireAsync([new("A")]).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ScopedCleanupFailureStillDisposesRootSingleton()
    {
        var disposed = false;
        var session = await TestSession.OpenAsync([], services =>
        {
            services.AddScoped(_ => new Cleanup(() => throw new ApplicationException("scoped")));
            services.AddSingleton(_ => new SingletonCleanup(() => disposed = true));
        });
        session.Services.GetRequiredService<Cleanup>();
        session.Services.GetRequiredService<SingletonCleanup>();
        await Assert.ThrowsAsync<AggregateException>(async () => await session.DisposeAsync());
        Assert.That(disposed, Is.True);
    }

    private sealed class Cleanup(Func<Task> cleanup) : IAsyncDisposable
    { public async ValueTask DisposeAsync() => await cleanup(); }
    private sealed class SingletonCleanup(Action cleanup) : IDisposable
    { public void Dispose() => cleanup(); }
}
