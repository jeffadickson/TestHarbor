namespace TestHarbor;

/// <summary>Identifies phases explicitly observed by a session.</summary>
public enum SessionPhase
{
    /// <summary>Extension startup is beginning.</summary>
    Starting,
    /// <summary>Startup completed and services are ready.</summary>
    Ready,
    /// <summary>An explicitly wrapped body is beginning.</summary>
    BodyStarting,
    /// <summary>An explicitly wrapped body finished or failed.</summary>
    BodyFinished,
    /// <summary>Extension and owned-resource cleanup is beginning.</summary>
    Cleaning,
    /// <summary>Cleanup exceeded its diagnostic threshold while retaining ownership.</summary>
    CleanupOverdue,
    /// <summary>All cleanup attempts and lease release have finished.</summary>
    Finished
}

/// <summary>Describes a session's observed outcome without claiming a final NUnit runner result.</summary>
/// <param name="Phase">Observed lifecycle boundary.</param>
/// <param name="Owner">Session identity.</param>
/// <param name="Timestamp">UTC observation time.</param>
/// <param name="Errors">Detached errors known at this boundary.</param>
/// <param name="IsFinal">True only for the session's final boundary, never the runner's result.</param>
public sealed record LifecycleObservation(SessionPhase Phase, string Owner, DateTimeOffset Timestamp,
    IReadOnlyList<Exception> Errors, bool IsFinal);
