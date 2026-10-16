namespace TestHarbor;

/// <summary>A detached acquisition observed at an audit boundary.</summary>
/// <param name="Owner">Caller-supplied owner label; labels need not be unique.</param>
/// <param name="Resources">Read-only normalized resource requests, possibly empty.</param>
/// <param name="Elapsed">Held duration for an active lease or queued duration for a waiter.</param>
public sealed record OwnershipEntry(string Owner, IReadOnlyList<ResourceRequest> Resources, TimeSpan Elapsed);

/// <summary>A detached quarantine entry, independent of outstanding ownership.</summary>
/// <param name="Resource">Resource blocked from reuse.</param>
/// <param name="Reason">Recorded recovery requirement.</param>
public sealed record QuarantineEntry(string Resource, string Reason);

/// <summary>An atomic, read-only observation of one coordinator, without changing ownership.</summary>
/// <param name="CapturedAt">UTC capture time.</param>
/// <param name="Active">Granted leases, including leases without resource names.</param>
/// <param name="Waiting">Pending acquisitions in FIFO order.</param>
/// <param name="Quarantined">Resources requiring recovery, whether held or released.</param>
public sealed record OwnershipAudit(DateTimeOffset CapturedAt, IReadOnlyList<OwnershipEntry> Active,
    IReadOnlyList<OwnershipEntry> Waiting, IReadOnlyList<QuarantineEntry> Quarantined)
{
    /// <summary>Gets whether no leases or queued requests remain; quarantine alone is not a leak.</summary>
    /// <value>True when active and waiting collections are empty.</value>
    public bool IsIdle => Active.Count == 0 && Waiting.Count == 0;

    /// <summary>Formats owner, resource and duration evidence for test output.</summary>
    /// <returns>A readable report including independent quarantine evidence.</returns>
    public override string ToString() => string.Join(Environment.NewLine,
        new[] { $"TestHarbor ownership audit: {Active.Count} active, {Waiting.Count} waiting, {Quarantined.Count} quarantined." }
        .Concat(Active.Select(x => Format("held", x)))
        .Concat(Waiting.Select(x => Format("waiting", x)))
        .Concat(Quarantined.Select(x => $"quarantined '{x.Resource}': {x.Reason}")));

    /// <summary>Formats one outstanding acquisition without losing empty-resource owners.</summary>
    /// <param name="state">Held or waiting label.</param>
    /// <param name="entry">Detached acquisition evidence.</param>
    /// <returns>A single diagnostic line.</returns>
    private static string Format(string state, OwnershipEntry entry) =>
        $"{state} owner '{entry.Owner}' for {entry.Elapsed:c}: " +
        (entry.Resources.Count == 0 ? "(no named resources)" :
            string.Join(", ", entry.Resources.Select(x => $"{x.Name} ({x.Access})")));
}

/// <summary>Signals outstanding ownership at a caller-selected end-of-run boundary.</summary>
public sealed class OutstandingOwnershipException : InvalidOperationException
{
    /// <summary>Creates an exception retaining the exact failed audit for structured reporting.</summary>
    /// <param name="audit">Snapshot containing outstanding ownership.</param>
    public OutstandingOwnershipException(OwnershipAudit audit) : base(audit.ToString()) => Audit = audit;

    /// <summary>Gets the detached snapshot used by the failed assertion.</summary>
    /// <value>The snapshot captured before the exception was thrown.</value>
    public OwnershipAudit Audit { get; }
}
