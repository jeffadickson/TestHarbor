using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace TestHarbor.Testing;

/// <summary>Captures extension behavior and session outcomes without depending on NUnit internals.</summary>
public sealed class ExtensionProbe
{
    /// <summary>Retains observations in their arrival order.</summary>
    private readonly ConcurrentQueue<LifecycleObservation> observations = new();
    /// <summary>Records named events emitted by extension authors and test bodies.</summary>
    private readonly ConcurrentQueue<string> events = new();

    /// <summary>Records an author-defined lifecycle event.</summary>
    /// <param name="name">Nonempty event label.</param>
    public void Record(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        events.Enqueue(name);
    }

    /// <summary>Runs a real session and captures its final exception and detached evidence.</summary>
    /// <param name="configure">Registers extensions and dependencies under test.</param>
    /// <param name="body">Optional body executed after extension startup.</param>
    /// <param name="cancellationToken">Cooperative startup/body cancellation.</param>
    /// <returns>Evidence including lifecycle events, observations, errors and lease release.</returns>
    /// <remarks>Use a fresh probe per run. This is a session harness, not a replacement test runner;
    /// separately test runner outcomes when implementing NUnit-specific adapters.</remarks>
    public async Task<ProbeResult> RunAsync(Action<IServiceCollection> configure,
        Func<TestSession, CancellationToken, Task>? body = null, CancellationToken cancellationToken = default)
    {
        var coordinator = new ResourceCoordinator();
        var resource = "probe/" + Guid.NewGuid().ToString("N");
        Exception? failure = null;
        try
        {
            await TestSession.RunAsync([new(resource)], body ?? ((_, _) => Task.CompletedTask), configure,
                coordinator, cancellationToken, new TestSessionOptions { Observer = observations.Enqueue });
        }
        catch (Exception error) { failure = error; }
        var state = coordinator.GetSnapshot().SingleOrDefault(x => x.Name == resource);
        return new(Array.AsReadOnly(events.ToArray()), Array.AsReadOnly(observations.ToArray()), failure,
            state is null || (state.Readers == 0 && !state.Exclusive), state?.QuarantineReason is not null);
    }
}

/// <summary>Contains detached conformance evidence from one session execution.</summary>
/// <param name="Events">Author-recorded event sequence.</param>
/// <param name="Observations">Session phase observations.</param>
/// <param name="Failure">Final session exception, or null for success.</param>
/// <param name="LeaseReleased">Whether resource ownership ended.</param>
/// <param name="Quarantined">Whether cleanup failure blocked reuse.</param>
public sealed record ProbeResult(IReadOnlyList<string> Events, IReadOnlyList<LifecycleObservation> Observations,
    Exception? Failure, bool LeaseReleased, bool Quarantined);
