namespace TestHarbor;

/// <summary>Defines cooperative phase deadlines and a cleanup diagnostic threshold.</summary>
/// <remarks>Deadlines request cancellation; they cannot stop uncooperative work. Cleanup always remains
/// awaited with ownership retained. A diagnostic threshold is not permission to release equipment.</remarks>
public sealed record SessionBudgets
{
    /// <summary>Gets the maximum cooperative acquisition wait.</summary>
    /// <value>Null for no additional deadline.</value>
    public TimeSpan? Acquisition { get; init; }
    /// <summary>Gets the cooperative extension startup deadline.</summary>
    /// <value>Null for no additional deadline.</value>
    public TimeSpan? Startup { get; init; }
    /// <summary>Gets the cooperative deadline for an explicitly wrapped test body.</summary>
    /// <value>Null for no additional deadline.</value>
    public TimeSpan? Body { get; init; }
    /// <summary>Gets the threshold after which cleanup emits an overdue observation.</summary>
    /// <value>Null to disable overdue diagnostics.</value>
    public TimeSpan? CleanupWarningAfter { get; init; }

    /// <summary>Validates all configured durations before any resource acquisition.</summary>
    internal void Validate()
    {
        foreach (var value in new[] { Acquisition, Startup, Body, CleanupWarningAfter })
            if (value is { } duration && (duration <= TimeSpan.Zero || duration.TotalMilliseconds > uint.MaxValue - 1))
                throw new ArgumentOutOfRangeException(nameof(value), "Budgets must be positive supported timer durations.");
    }

    /// <summary>Creates a phase-local token linked to caller cancellation.</summary>
    /// <param name="duration">Additional deadline, or null.</param>
    /// <param name="parent">Caller cancellation.</param>
    /// <returns>A source that the phase owner must dispose.</returns>
    internal static CancellationTokenSource Start(TimeSpan? duration, CancellationToken parent)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(parent);
        if (duration is { } value) source.CancelAfter(value);
        return source;
    }
}
