using System.Diagnostics;

namespace TestHarbor;

/// <summary>Coordinates atomic sets of named resources within one process.</summary>
/// <remarks>Requests are FIFO. The oldest blocked request can delay unrelated requests.
/// Leases are not reentrant; declare every needed resource in one request.</remarks>
public sealed class ResourceCoordinator
{
    private readonly object gate = new();
    private readonly Dictionary<string, (int Readers, bool Writer)> held = new(StringComparer.Ordinal);
    private readonly LinkedList<Pending> queue = new();
    private readonly Dictionary<string, string> quarantine = new(StringComparer.Ordinal);
    private readonly HashSet<Pending> active = [];
    private readonly Queue<ResourceEvent> events = new();

    /// <summary>Returns detached resource state, including current owner labels and wait counts.</summary>
    public IReadOnlyList<ResourceStatus> GetSnapshot()
    {
        lock (gate)
        {
            var names = held.Keys.Concat(quarantine.Keys).Concat(queue.SelectMany(x => x.Items.Select(i => i.Name)))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
            return Array.AsReadOnly(names.Select(name =>
            {
                held.TryGetValue(name, out var state);
                quarantine.TryGetValue(name, out var reason);
                return new ResourceStatus(name, state.Readers, state.Writer,
                    queue.Count(x => x.Items.Any(i => i.Name == name)), reason,
                    Array.AsReadOnly(active.Where(x => x.Items.Any(i => i.Name == name)).Select(x => x.Owner).ToArray()));
            }).ToArray());
        }
    }

    /// <summary>Returns the latest 256 events in chronological order.</summary>
    public IReadOnlyList<ResourceEvent> GetEvents()
    { lock (gate) return Array.AsReadOnly(events.ToArray()); }

    /// <summary>Clears a quarantine after caller-controlled recovery and after all owners leave.</summary>
    /// <param name="name">Resource recovered by the caller.</param>
    /// <param name="recoveryReason">Nonempty audit explanation; this method does not recover physical hardware.</param>
    public void ResetQuarantine(string name, string recoveryReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryReason);
        lock (gate)
        {
            if (held.ContainsKey(name)) throw new InvalidOperationException("Cannot recover a resource while it is held.");
            if (!quarantine.Remove(name)) throw new InvalidOperationException("Resource is not quarantined.");
            AddEvent("Reset", recoveryReason, name, TimeSpan.Zero);
            Drain();
        }
    }

    private void AddEvent(string kind, string owner, string names, TimeSpan elapsed)
    {
        events.Enqueue(new(DateTimeOffset.UtcNow, kind, owner, names, elapsed));
        while (events.Count > 256) events.Dequeue();
    }

    private ResourceQuarantinedException? QuarantineError(ResourceRequest[] items)
    {
        foreach (var item in items)
            if (quarantine.TryGetValue(item.Name, out var reason)) return new(item.Name, reason);
        return null;
    }

    internal void Quarantine(ResourceRequest[] items, string reason, string owner)
    {
        lock (gate)
        {
            foreach (var item in items) quarantine[item.Name] = reason;
            AddEvent("Quarantined", owner, string.Join(", ", items.Select(x => x.Name)), TimeSpan.Zero);
            // Reject every affected waiter now, not just the FIFO head.
            var node = queue.First;
            while (node is not null)
            {
                var next = node.Next;
                if (QuarantineError(node.Value.Items) is { } error)
                {
                    queue.Remove(node);
                    AddEvent("Rejected", node.Value.Owner, node.Value.Names, Stopwatch.GetElapsedTime(node.Value.Created));
                    node.Value.Completion.TrySetException(error);
                }
                node = next;
            }
            Drain();
        }
    }

    /// <summary>Gets the coordinator shared by default across TestHarbor sessions in this process.</summary>
    public static ResourceCoordinator Shared { get; } = new();

    /// <summary>Waits for and atomically acquires all requested resources.</summary>
    /// <param name="requests">Requirements; duplicates merge to the strongest access mode.</param>
    /// <param name="cancellationToken">Cancels waiting, but does not revoke a granted lease.</param>
    /// <param name="owner">Optional diagnostic label; a unique label is generated when omitted.</param>
    /// <returns>An idempotently disposable lease; retain it until cleanup is complete.</returns>
    public async Task<ResourceLease> AcquireAsync(IEnumerable<ResourceRequest> requests,
        CancellationToken cancellationToken = default, string? owner = null)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var items = requests.ToArray();
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentException.ThrowIfNullOrWhiteSpace(item.Name);
            if (!Enum.IsDefined(item.Access)) throw new ArgumentOutOfRangeException(nameof(requests));
        }
        var normalized = items.GroupBy(x => x.Name, StringComparer.Ordinal)
            .Select(g => new ResourceRequest(g.Key, g.Any(x => x.Access == ResourceAccess.Exclusive)
                ? ResourceAccess.Exclusive : ResourceAccess.SharedRead)).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        var pending = new Pending(normalized, owner ?? Guid.NewGuid().ToString("N"));
        lock (gate)
        {
            if (QuarantineError(normalized) is { } error)
            {
                AddEvent("Rejected", pending.Owner, pending.Names, TimeSpan.Zero);
                throw error;
            }
            pending.Node = queue.AddLast(pending);
            AddEvent("Queued", pending.Owner, pending.Names, TimeSpan.Zero);
            Drain();
        }
        // Registration after enqueue is safe: a pre-cancelled token runs the callback immediately.
        using var registration = cancellationToken.Register(() => Cancel(pending, cancellationToken));
        return await pending.Completion.Task.ConfigureAwait(false);
    }

    private void Cancel(Pending pending, CancellationToken token)
    {
        lock (gate)
        {
            if (pending.Node?.List is null) return; // Grant won the race; the caller owns its lease.
            queue.Remove(pending.Node);
            AddEvent("Cancelled", pending.Owner, pending.Names, Stopwatch.GetElapsedTime(pending.Created));
            pending.Completion.TrySetCanceled(token);
            Drain();
        }
    }

    private void Drain()
    {
        while (queue.First is { } node)
        {
            var request = node.Value;
            if (request.Items.Any(x => held.TryGetValue(x.Name, out var state)
                && (state.Writer || x.Access == ResourceAccess.Exclusive && state.Readers > 0))) return;
            queue.RemoveFirst();
            foreach (var item in request.Items)
            {
                held.TryGetValue(item.Name, out var state);
                held[item.Name] = item.Access == ResourceAccess.Exclusive
                    ? (0, true) : (state.Readers + 1, false);
            }
            request.Granted = Stopwatch.GetTimestamp();
            active.Add(request);
            AddEvent("Granted", request.Owner, request.Names, Stopwatch.GetElapsedTime(request.Created));
            request.Completion.SetResult(new ResourceLease(this, request.Items, request.Owner, () => Release(request)));
        }
    }

    private void Release(Pending request)
    {
        lock (gate)
        {
            foreach (var item in request.Items)
            {
                var state = held[item.Name];
                if (state.Writer || state.Readers == 1) held.Remove(item.Name);
                else held[item.Name] = (state.Readers - 1, false);
            }
            active.Remove(request);
            AddEvent("Released", request.Owner, request.Names, Stopwatch.GetElapsedTime(request.Granted));
            Drain();
        }
    }

    private sealed class Pending(ResourceRequest[] items, string owner)
    {
        internal string Owner { get; } = owner;
        internal string Names => string.Join(", ", Items.Select(x => x.Name));
        internal long Created { get; } = Stopwatch.GetTimestamp();
        internal long Granted { get; set; }
        internal ResourceRequest[] Items { get; } = items;
        internal LinkedListNode<Pending>? Node { get; set; }
        internal TaskCompletionSource<ResourceLease> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

}
