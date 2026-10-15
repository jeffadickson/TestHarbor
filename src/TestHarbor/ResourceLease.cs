namespace TestHarbor;

/// <summary>An acquired resource set; quarantine before release if cleanup cannot establish safe reuse.</summary>
public sealed class ResourceLease : IDisposable
{
    /// <summary>Serializes access to mutable ownership state.</summary>
    private readonly object gate = new();
    /// <summary>Identifies the coordinator that granted and tracks this lease.</summary>
    private readonly ResourceCoordinator coordinator;
    /// <summary>Contains the normalized resources granted to this lease.</summary>
    private readonly ResourceRequest[] items;
    /// <summary>Identifies this lease owner in resource diagnostics.</summary>
    private readonly string owner;
    /// <summary>Holds the one-shot ownership release callback until disposal consumes it.</summary>
    private Action? release;
    /// <summary>Binds a granted resource set to its coordinator and one-shot release callback.</summary>
    /// <param name="coordinator">Coordinator that granted the resources.</param>
    /// <param name="items">Normalized resource requirements involved in this operation.</param>
    /// <param name="owner">Diagnostic label identifying the acquisition or session.</param>
    /// <param name="release">Callback that updates coordinator ownership exactly once.</param>
    internal ResourceLease(ResourceCoordinator coordinator, ResourceRequest[] items, string owner, Action release)
    { this.coordinator = coordinator; this.items = items; this.owner = owner; this.release = release; }

    /// <summary>Blocks future owners without revoking existing owners, while retaining this lease.</summary>
    /// <param name="reason">Explanation of why explicit recovery is required.</param>
    /// <exception cref="ObjectDisposedException">Ownership has already been released.</exception>
    /// <exception cref="ArgumentException">The quarantine reason is empty.</exception>
    public void Quarantine(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(release is null, this);
            coordinator.Quarantine(items, reason, owner);
        }
    }

    /// <summary>Releases ownership once; an existing quarantine remains in effect.</summary>
    public void Dispose()
    {
        lock (gate) { var action = release; release = null; action?.Invoke(); }
    }
}
