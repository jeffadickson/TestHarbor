namespace TestHarbor;

/// <summary>A detached snapshot of one resource's ownership and quarantine state.</summary>
/// <param name="Name">Case-sensitive resource name.</param>
/// <param name="Readers">Current shared owner count.</param>
/// <param name="Exclusive">Whether an exclusive owner holds this resource.</param>
/// <param name="Waiting">Number of queued requests requiring this resource.</param>
/// <param name="QuarantineReason">Reason for blocking new owners, or null.</param>
/// <param name="Owners">Detached, read-only current owner labels.</param>
public sealed record ResourceStatus(string Name, int Readers, bool Exclusive, int Waiting,
    string? QuarantineReason, IReadOnlyList<string> Owners);

/// <summary>A resource-coordination event in the bounded process-local history.</summary>
/// <param name="Timestamp">UTC time of the event.</param>
/// <param name="Kind">Queued, Granted, Cancelled, Released, Quarantined, Rejected, or Reset.</param>
/// <param name="Owner">Caller label, or recovery reason for Reset.</param>
/// <param name="Resources">Names involved, formatted for diagnostics.</param>
/// <param name="Elapsed">Queue wait for Granted/Cancelled; ownership time for Released.</param>
public sealed record ResourceEvent(DateTimeOffset Timestamp, string Kind, string Owner,
    string Resources, TimeSpan Elapsed);

/// <summary>Indicates that a requested resource requires explicit recovery before reuse.</summary>
public sealed class ResourceQuarantinedException : InvalidOperationException
{
    /// <summary>Creates a failure identifying the quarantined resource and reason.</summary>
    /// <param name="resource">Resource name.</param>
    /// <param name="reason">Recorded cleanup or operator failure.</param>
    public ResourceQuarantinedException(string resource, string reason)
        : base($"Resource '{resource}' is quarantined: {reason}") { }
}
