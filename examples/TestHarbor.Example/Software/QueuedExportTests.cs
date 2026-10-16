using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace TestHarbor.Example.Software;

/// <summary>Proves that a queued consumer cannot observe an export before asynchronous cleanup finishes.</summary>
[Parallelizable(ParallelScope.All)]
public sealed class QueuedExportTests
{
    /// <summary>Exercises successful finalization and rejection of a waiting consumer after failed finalization.</summary>
    /// <param name="failCleanup">Whether finalization reports an injected failure before publishing complete output.</param>
    /// <returns>A task completing after ownership, output and queued-consumer outcomes have been verified.</returns>
    [TestCase(false)]
    [TestCase(true)]
    public async Task WaitingConsumerReceivesCompletedOutputOrQuarantine(bool failCleanup)
    {
        var directory = Directory.CreateTempSubdirectory("testharbor-queued-export-");
        var path = Path.Combine(directory.FullName, "export.txt");
        var coordinator = new ResourceCoordinator();
        var finalizer = new ExportFinalizer(path, failCleanup);
        Task? cleanup = null;
        Task? consumer = null;
        TestSession? producer = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            producer = await TestSession.OpenAsync([new("Exports/Orders")],
                services => services.AddScoped<ITestExtension>(_ => finalizer), coordinator,
                cancellationToken: deadline.Token);
            await File.WriteAllTextAsync(path, "unfinished", deadline.Token);
            cleanup = producer.DisposeAsync().AsTask();
            await finalizer.Entered.Task.WaitAsync(deadline.Token);
            var bodyRan = false;
            consumer = TestSession.RunAsync([new("Exports/Orders")], async (_, token) =>
            {
                bodyRan = true;
                Assert.That(await File.ReadAllTextAsync(path, token), Is.EqualTo("complete"));
            }, coordinator: coordinator, cancellationToken: deadline.Token);

            // AcquireAsync enqueues synchronously before its first incomplete await.
            var status = coordinator.GetSnapshot().Single(x => x.Name == "Exports/Orders");
            Assert.That(status.Exclusive, Is.True);
            Assert.That(status.Waiting, Is.EqualTo(1));
            Assert.That(consumer.IsCompleted, Is.False);
            Assert.That(bodyRan, Is.False);
            Assert.That(await File.ReadAllTextAsync(path, deadline.Token), Is.EqualTo("unfinished"));
            TestContext.Out.WriteLine("[Software/queued export] The producer body has finished, but TestHarbor still owns the output during asynchronous finalization. The consumer is verifiably queued, not merely scheduled later.");

            finalizer.Continue.TrySetResult();
            if (failCleanup)
            {
                var error = await Assert.ThrowsAsync<AggregateException>(async () => await cleanup.WaitAsync(deadline.Token));
                Assert.That(error!.Flatten().InnerExceptions.Single(), Is.TypeOf<IOException>());
                await Assert.ThrowsAsync<ResourceQuarantinedException>(async () => await consumer.WaitAsync(deadline.Token));
                Assert.That(bodyRan, Is.False);
                Assert.That(await File.ReadAllTextAsync(path, deadline.Token), Is.EqualTo("unfinished"));
                var quarantined = coordinator.GetSnapshot().Single(x => x.Name == "Exports/Orders");
                Assert.That(quarantined.Exclusive, Is.False);
                Assert.That(quarantined.Waiting, Is.Zero);
                Assert.That(quarantined.QuarantineReason, Is.Not.Null);
                TestContext.Out.WriteLine("[Software/queued export] Finalization failed: TestHarbor rejected the already-waiting consumer before its body. A manual semaphore released in finally would permit reuse unless the suite also implemented a failure policy.");
            }
            else
            {
                await cleanup.WaitAsync(deadline.Token);
                await consumer.WaitAsync(deadline.Token);
                Assert.That(bodyRan, Is.True);
                Assert.That(coordinator.GetSnapshot(), Is.Empty);
                TestContext.Out.WriteLine("[Software/queued export] Finalization completed before ownership transferred; the consumer read only complete output.");
            }

            var unrelatedRan = false;
            await TestSession.RunAsync([new("Exports/Invoices")], (_, _) =>
            {
                unrelatedRan = true;
                return Task.CompletedTask;
            }, coordinator: coordinator, cancellationToken: deadline.Token);
            Assert.That(unrelatedRan, Is.True, "A quarantined orders export must not poison other resource names.");
        }
        finally
        {
            finalizer.Continue.TrySetResult();
            // Observe all operations even when an assertion fails, before removing their files.
            if (producer is not null) cleanup ??= producer.DisposeAsync().AsTask();
            if (cleanup is not null) { try { await cleanup; } catch { /* Asserted in the main path. */ } }
            if (consumer is not null) { try { await consumer; } catch { /* Asserted in the main path. */ } }
            directory.Delete(recursive: true);
        }
    }

    /// <summary>Models an asynchronous exporter drain using explicit barriers and real file operations.</summary>
    /// <param name="path">Output file finalized during cleanup.</param>
    /// <param name="fail">Whether to inject a finalization failure.</param>
    private sealed class ExportFinalizer(string path, bool fail) : ITestExtension
    {
        /// <summary>Signals that cleanup has begun while ownership is still retained.</summary>
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>Allows the test to finish finalization after proving a consumer is queued.</summary>
        internal readonly TaskCompletionSource Continue = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Starts without modifying the file written by the producer body.</summary>
        /// <param name="context">Current session services and identity.</param>
        /// <param name="cancellationToken">Startup cancellation.</param>
        /// <returns>A completed startup task.</returns>
        public Task StartAsync(TestExtensionContext context, CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>Waits for the controlled drain, then publishes complete output or reports failure.</summary>
        /// <param name="context">Session context available during cleanup.</param>
        /// <returns>A task completing once output is finalized, or failing before finalization.</returns>
        public async Task StopAsync(TestExtensionContext context)
        {
            Entered.TrySetResult();
            await Continue.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (fail) throw new IOException("Injected export finalization failure.");
            await File.WriteAllTextAsync(path, "complete");
        }
    }
}
