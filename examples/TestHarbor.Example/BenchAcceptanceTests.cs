using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor.NUnit;

namespace TestHarbor.Example;

/// <summary>Exercises a simulated supply and electronic load using ordinary NUnit cases and TestHarbor sessions.</summary>
/// <remarks>Each case owns its simulated bench; competing sessions within a case share that physical model.</remarks>
[Parallelizable(ParallelScope.All)]
public sealed class BenchAcceptanceTests
{
    /// <summary>Measures a resistive load, retains evidence and verifies output shutdown.</summary>
    /// <param name="volts">Supply setpoint in volts.</param>
    /// <param name="ohms">Simulated load resistance in ohms.</param>
    /// <returns>A task completing after measurement, evidence publication and cleanup.</returns>
    [TestCase(3.3, 100.0)]
    [TestCase(5.0, 50.0)]
    [TestCase(12.0, 120.0)]
    public async Task MeasuresLoadAndRetainsEvidence(double volts, double ohms)
    {
        var bench = new Bench();
        var coordinator = new ResourceCoordinator();
        await TestSession.RunAsync(Resources(), async (session, token) =>
        {
            var connection = session.Services.GetRequiredService<Connection>();
            var current = await connection.MeasureAsync(volts, ohms, token);
            Assert.That(current, Is.EqualTo(volts / ohms).Within(0.000001));
            var csv = string.Create(CultureInfo.InvariantCulture, $"volts,ohms,amps\n{volts},{ohms},{current}\n");
            var store = new ArtifactStore(Path.Combine(TestContext.CurrentContext.WorkDirectory,
                "TestResults", "bench-evidence", Guid.NewGuid().ToString("N")));
            var evidence = await store.PublishTextAsync(csv, "measurement.csv", "load-sweep", cancellationToken: token);
            HarborArtifacts.Attach(evidence);
            Assert.That(await File.ReadAllTextAsync(evidence.Path, token), Is.EqualTo(csv));
            Explain("MEASUREMENT", "NUnit checks the electrical result. TestHarbor publishes the CSV with owner, time and hash metadata for NUnit attachments.");
        }, services => Register(services, bench, coordinator), coordinator);
        Assert.That(bench.Enabled, Is.False);
        Assert.That(bench.Active, Is.Zero);
        Assert.That(bench.Events.ToArray(), Is.EqualTo(new[] { "connect", "measure", "output-off", "disconnect" }));
        Explain("CLEANUP", "The session awaited output shutdown and connection disposal before releasing both instrument resources; this policy would need explicit plumbing in a plain NUnit fixture.");
    }

    /// <summary>Runs many sessions against one bench and verifies exclusive ownership and isolated clients.</summary>
    /// <param name="count">Number of independently scoped measurement sessions.</param>
    /// <returns>A task completing after every competing session has cleaned up.</returns>
    [TestCase(2)]
    [TestCase(32)]
    public async Task CompetingMeasurementsRemainIsolated(int count)
    {
        var bench = new Bench();
        var coordinator = new ResourceCoordinator();
        var clients = new ConcurrentBag<Guid>();
        await Task.WhenAll(Enumerable.Range(0, count).Select(index => TestSession.RunAsync(
            index % 2 == 0 ? Resources() : Resources().Reverse(), async (session, token) =>
            {
                var connection = session.Services.GetRequiredService<Connection>();
                clients.Add(connection.Id);
                Assert.That(await connection.MeasureAsync(index + 1, 100, token), Is.EqualTo((index + 1) / 100.0));
            }, services => Register(services, bench, coordinator), coordinator,
            options: new() { Owner = "measurement-" + index })));
        Assert.That(clients.Distinct().Count(), Is.EqualTo(count));
        Assert.That(bench.MaximumActive, Is.EqualTo(1));
        Assert.That(bench.Active, Is.Zero);
        Assert.That(bench.Enabled, Is.False);
        Explain("CONTENTION", $"{count} sessions used distinct DI clients; maximum simultaneous bench owners was one, even with reversed resource declarations. NUnit runs the test; TestHarbor atomically acquires the named supply/load pair.");
    }

    /// <summary>Proves that a waiting measurement cannot start while async output shutdown is paused.</summary>
    /// <returns>A task completing after both sessions and the paused cleanup are joined.</returns>
    [Test]
    public async Task OwnershipSpansAsynchronousShutdown()
    {
        var bench = new Bench { PauseCleanup = true };
        var coordinator = new ResourceCoordinator();
        var first = await TestSession.OpenAsync(Resources(), services => Register(services, bench, coordinator), coordinator);
        var cleanup = first.DisposeAsync().AsTask();
        Task<TestSession>? waiting = null;
        try
        {
            await bench.CleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // An unrelated bench can operate while this lease is active. Queue FIFO is a separate policy.
            using var other = await coordinator.AcquireAsync([new("OtherBench/Meter")]);
            waiting = TestSession.OpenAsync(Resources(), services => Register(services, bench, coordinator), coordinator);
            Assert.That(waiting.IsCompleted, Is.False);
            Assert.That(coordinator.GetSnapshot().Where(x => x.Name.StartsWith("Bench/", StringComparison.Ordinal))
                .All(x => x.Exclusive && x.Waiting == 1), Is.True);
            Explain("OWNERSHIP", "The next measurement is queued while output shutdown is paused; an unrelated bench still acquired its resource. A test-body-only lock would end too early for this cleanup boundary.");
        }
        finally
        {
            bench.ContinueCleanup.TrySetResult();
            await cleanup;
            if (waiting is not null) await (await waiting).DisposeAsync();
        }
        Assert.That(bench.Active, Is.Zero);
        Assert.That(bench.MaximumActive, Is.EqualTo(1));
    }

    /// <summary>Preserves a measurement failure and shutdown failure, blocks reuse and requires explicit recovery.</summary>
    /// <returns>A task completing after verified simulated recovery and successful reuse.</returns>
    [Test]
    public async Task FailedShutdownQuarantinesBothInstruments()
    {
        var bench = new Bench { FailShutdown = true };
        var coordinator = new ResourceCoordinator();
        var error = await Assert.ThrowsAsync<AggregateException>(async () => await TestSession.RunAsync(Resources(),
            (_, _) => throw new InvalidOperationException("Measurement outside tolerance"),
            services => Register(services, bench, coordinator), coordinator));
        var failures = error!.Flatten().InnerExceptions;
        Assert.That(failures.Any(x => x.Message == "Measurement outside tolerance"), Is.True);
        Assert.That(failures.Any(x => x.Message == "Output shutdown unconfirmed"), Is.True);
        Assert.That(bench.Active, Is.Zero);
        Assert.That(bench.Enabled, Is.True, "Disconnect does not prove physical output is safe.");
        Assert.That(coordinator.GetSnapshot().All(x => x.QuarantineReason is not null && !x.Exclusive), Is.True);
        await Assert.ThrowsAsync<ResourceQuarantinedException>(async () =>
            await TestSession.OpenAsync(Resources(), services => Register(services, bench, coordinator), coordinator));
        Assert.That(bench.Events.Count(x => x == "connect"), Is.EqualTo(1));
        Explain("FAILURE", "Both the measurement error and shutdown error survived. Ownership was released, but both instruments are quarantined and reuse failed before connection startup. NUnit assertions verify this TestHarbor recovery policy.");
        bench.Recover();
        foreach (var resource in Resources()) coordinator.ResetQuarantine(resource.Name, "Simulated outputs inspected and reset.");
        await using (var recovered = await TestSession.OpenAsync(Resources(), services => Register(services, bench, coordinator), coordinator))
            Assert.That(await recovered.Services.GetRequiredService<Connection>().MeasureAsync(5, 100, default), Is.EqualTo(0.05));
        Assert.That(bench.Enabled, Is.False);
        Explain("RECOVERY", "Reuse succeeded only after simulated device recovery and explicit quarantine reset. Resetting bookkeeping alone would not make physical hardware safe.");
    }

    /// <summary>Cancels a queued measurement without revoking the active owner's instrument lease.</summary>
    /// <returns>A task completing after cancellation and owner cleanup.</returns>
    [Test]
    public async Task CancellingWaiterDoesNotInterruptActiveMeasurement()
    {
        var bench = new Bench();
        var coordinator = new ResourceCoordinator();
        await using var owner = await TestSession.OpenAsync(Resources(), services => Register(services, bench, coordinator), coordinator);
        using var cancellation = new CancellationTokenSource();
        var waiting = TestSession.OpenAsync(Resources(), services => Register(services, bench, coordinator), coordinator, cancellation.Token);
        Assert.That(waiting.IsCompleted, Is.False);
        cancellation.Cancel();
        await Assert.CatchAsync<OperationCanceledException>(async () => await waiting);
        Assert.That(bench.Events.Count(x => x == "connect"), Is.EqualTo(1));
        Assert.That(coordinator.GetSnapshot().All(x => x.Exclusive && x.Waiting == 0), Is.True);
        Assert.That(await owner.Services.GetRequiredService<Connection>().MeasureAsync(5, 100, default), Is.EqualTo(0.05));
        Explain("CANCELLATION", "Cancelling the queued session removed only its request; the active measurement kept ownership and completed. This is resource-wait policy layered on NUnit's test execution.");
    }

    /// <summary>Returns the complete atomic resource set for a supply/load measurement.</summary>
    /// <returns>Exclusive supply and electronic-load requests.</returns>
    private static ResourceRequest[] Resources() => [new("Bench/Supply"), new("Bench/Load")];

    /// <summary>Registers a fresh connection and its cleanup extension without opening equipment during registration.</summary>
    /// <param name="services">Session-local registrations.</param>
    /// <param name="bench">Shared physical simulation.</param>
    /// <param name="coordinator">Coordinator used to independently verify ownership during callbacks.</param>
    private static void Register(IServiceCollection services, Bench bench, ResourceCoordinator coordinator)
    {
        services.AddScoped(_ => new Connection(bench, coordinator));
        services.AddScoped<ITestExtension, ConnectionLifecycle>();
    }

    /// <summary>Emits a verified explanation both live and into the NUnit test's captured output.</summary>
    /// <param name="stage">Short scenario label.</param>
    /// <param name="message">Explanation of the behavior just asserted.</param>
    private static void Explain(string stage, string message)
    {
        var line = $"[TestHarbor demo: {stage}] {message}";
        TestContext.Progress.WriteLine(line);
        TestContext.Out.WriteLine(line);
    }

    /// <summary>Models physical bench state separately from per-session connection objects.</summary>
    private sealed class Bench
    {
        /// <summary>Counts open clients to detect overlapping physical access independently of coordinator state.</summary>
        public int Active;
        /// <summary>Records the maximum concurrent clients observed by the simulation.</summary>
        public int MaximumActive;
        /// <summary>Represents the physical output state, which can survive a failed shutdown and disconnect.</summary>
        public bool Enabled;
        /// <summary>Retains the physical supply setpoint across asynchronous reads.</summary>
        public double Volts;
        /// <summary>Retains the physical electronic-load resistance across asynchronous reads.</summary>
        public double Ohms;
        /// <summary>Selects deterministic shutdown failure injection.</summary>
        public bool FailShutdown;
        /// <summary>Selects a cleanup barrier for ownership-boundary verification.</summary>
        public bool PauseCleanup;
        /// <summary>Records independently observable device events.</summary>
        public readonly ConcurrentQueue<string> Events = new();
        /// <summary>Signals entry into asynchronous shutdown.</summary>
        public readonly TaskCompletionSource CleanupEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>Releases the deterministic shutdown barrier.</summary>
        public readonly TaskCompletionSource ContinueCleanup = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Simulates operator inspection and output reset after every client has disconnected.</summary>
        public void Recover()
        {
            Assert.That(Active, Is.Zero);
            Enabled = false;
            FailShutdown = false;
        }
    }

    /// <summary>Models a per-session instrument client whose lifecycle independently checks the held resource set.</summary>
    /// <param name="bench">Shared physical bench state.</param>
    /// <param name="coordinator">Coordinator whose ownership must cover all device access.</param>
    private sealed class Connection(Bench bench, ResourceCoordinator coordinator) : IAsyncDisposable
    {
        /// <summary>Identifies this client independently of shared physical state.</summary>
        public readonly Guid Id = Guid.NewGuid();
        /// <summary>Tracks successful connection startup for partial-initialization cleanup.</summary>
        private bool connected;

        /// <summary>Checks both instruments are owned before performing device operations.</summary>
        private void VerifyOwnership() => Assert.That(Resources().All(resource =>
            coordinator.GetSnapshot().Any(state => state.Name == resource.Name && state.Exclusive)), Is.True);

        /// <summary>Opens the simulated connection only after the complete resource set is acquired.</summary>
        public void Connect()
        {
            VerifyOwnership();
            connected = true;
            var active = Interlocked.Increment(ref bench.Active);
            Interlocked.Exchange(ref bench.MaximumActive, Math.Max(bench.MaximumActive, active));
            Assert.That(active, Is.EqualTo(1), "Physical access overlapped.");
            bench.Enabled = true;
            bench.Events.Enqueue("connect");
        }

        /// <summary>Measures deterministic load current after an asynchronous device operation.</summary>
        /// <param name="volts">Requested supply voltage.</param>
        /// <param name="ohms">Load resistance.</param>
        /// <param name="token">Cooperative measurement cancellation.</param>
        /// <returns>Simulated current in amperes.</returns>
        public async Task<double> MeasureAsync(double volts, double ohms, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            VerifyOwnership();
            Assert.That(connected && bench.Enabled, Is.True);
            bench.Volts = volts;
            bench.Ohms = ohms;
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            bench.Events.Enqueue("measure");
            return bench.Volts / bench.Ohms;
        }

        /// <summary>Attempts shutdown while retaining ownership, optionally pausing or injecting failure.</summary>
        /// <returns>A task completing when output shutdown is confirmed.</returns>
        public async Task ShutdownAsync()
        {
            VerifyOwnership();
            if (!connected) return;
            bench.CleanupEntered.TrySetResult();
            if (bench.PauseCleanup) await bench.ContinueCleanup.Task;
            if (bench.FailShutdown) throw new IOException("Output shutdown unconfirmed");
            bench.Enabled = false;
            bench.Events.Enqueue("output-off");
        }

        /// <summary>Disconnects while ownership is retained without pretending disconnection disables physical output.</summary>
        /// <returns>A completed async disposal operation.</returns>
        public ValueTask DisposeAsync()
        {
            VerifyOwnership();
            if (connected)
            {
                connected = false;
                Interlocked.Decrement(ref bench.Active);
                bench.Events.Enqueue("disconnect");
            }
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Connects and shuts down equipment through the session's ordered extension lifecycle.</summary>
    /// <param name="connection">DI-owned client disposed after extension shutdown.</param>
    private sealed class ConnectionLifecycle(Connection connection) : ITestExtension
    {
        /// <summary>Opens equipment under the acquired resource lease.</summary>
        /// <param name="context">Session services and owner identity.</param>
        /// <param name="cancellationToken">Cooperative startup cancellation.</param>
        /// <returns>A completed startup task.</returns>
        public Task StartAsync(TestExtensionContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            connection.Connect();
            return Task.CompletedTask;
        }

        /// <summary>Awaits physical output shutdown before DI disposes the client.</summary>
        /// <param name="context">Session services still valid during cleanup.</param>
        /// <returns>The asynchronous shutdown task.</returns>
        public Task StopAsync(TestExtensionContext context) => connection.ShutdownAsync();
    }
}
