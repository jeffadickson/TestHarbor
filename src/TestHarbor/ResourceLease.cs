namespace TestHarbor;

/// <summary>An acquired resource set; quarantine before release if cleanup cannot establish safe reuse.</summary>
public sealed class ResourceLease : IDisposable
{
    private readonly object gate = new();
    private readonly ResourceCoordinator coordinator;
    private readonly ResourceRequest[] items;
    private readonly string owner;
    private Action? release;
    internal ResourceLease(ResourceCoordinator coordinator, ResourceRequest[] items, string owner, Action release)
    { this.coordinator = coordinator; this.items = items; this.owner = owner; this.release = release; }

    /// <summary>Blocks future owners without revoking existing owners, while retaining this lease.</summary>
    /// <param name="reason">Explanation of why explicit recovery is required.</param>
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
