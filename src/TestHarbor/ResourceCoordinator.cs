using System.Diagnostics;

namespace TestHarbor;

/// <summary>Coordinates atomic sets of named resources within one process.</summary>
/// <remarks>Requests are FIFO. The oldest blocked request can delay unrelated requests.
/// Leases are not reentrant; declare every needed resource in one request.</remarks>
public sealed class ResourceCoordinator
{
    /// <summary>Serializes access to mutable ownership state.</summary>
    private readonly object gate = new();
    /// <summary>Tracks reader counts and exclusive ownership for currently held resources.</summary>
    private readonly Dictionary<string, (int Readers, bool Writer)> held = new(StringComparer.Ordinal);
    /// <summary>Stores pending atomic acquisitions in FIFO order.</summary>
    private readonly LinkedList<Pending> queue = new();
    /// <summary>Retains resource failure reasons until explicit recovery.</summary>
    private readonly Dictionary<string, string> quarantine = new(StringComparer.Ordinal);
    /// <summary>Tracks granted requests for owner diagnostics until their leases are released.</summary>
    private readonly HashSet<Pending> active = [];
    /// <summary>Retains the most recent resource coordination events, capped at 256 entries.</summary>
    private readonly Queue<ResourceEvent> events = new();

    /// <summary>Returns detached resource state, including current owner labels and wait counts.</summary>
    /// <returns>A detached read-only snapshot of active, waiting and quarantined resources.</returns>
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

    /// <summary>Atomically audits every lease and waiter, including empty-resource sessions.</summary>
    /// <returns>A detached read-only snapshot; this method never releases or cancels work.</returns>
    public OwnershipAudit GetOwnershipAudit()
    {
        lock (gate)
        {
            var now = Stopwatch.GetTimestamp();
            return new(DateTimeOffset.UtcNow,
                Array.AsReadOnly(active.OrderBy(x => x.Owner, StringComparer.Ordinal).ThenBy(x => x.Created)
                    .Select(x => new OwnershipEntry(x.Owner, Array.AsReadOnly(x.Items.ToArray()),
                        Stopwatch.GetElapsedTime(x.Granted, now))).ToArray()),
                Array.AsReadOnly(queue.Select(x => new OwnershipEntry(x.Owner, Array.AsReadOnly(x.Items.ToArray()),
                    Stopwatch.GetElapsedTime(x.Created, now))).ToArray()),
                Array.AsReadOnly(quarantine.OrderBy(x => x.Key, StringComparer.Ordinal)
                    .Select(x => new QuarantineEntry(x.Key, x.Value)).ToArray()));
        }
    }

    /// <summary>Fails if ownership remains at an explicitly chosen, quiescent suite boundary.</summary>
    /// <remarks>Join all suite work first. This is a point-in-time check, not a shutdown gate.
    /// Quarantine alone does not fail the ownership check and is never cleared.</remarks>
    /// <exception cref="OutstandingOwnershipException">Active leases or queued acquisitions remain.</exception>
    public void ThrowIfNotIdle()
    {
        var audit = GetOwnershipAudit();
        if (!audit.IsIdle) throw new OutstandingOwnershipException(audit);
    }

    /// <summary>Returns the latest 256 events in chronological order.</summary>
    /// <returns>A detached chronological list containing at most 256 recent events.</returns>
    public IReadOnlyList<ResourceEvent> GetEvents()
    { lock (gate) return Array.AsReadOnly(events.ToArray()); }

    /// <summary>Clears a quarantine after caller-controlled recovery and after all owners leave.</summary>
    /// <param name="name">Resource recovered by the caller.</param>
    /// <param name="recoveryReason">Nonempty audit explanation; this method does not recover physical hardware.</param>
    /// <exception cref="InvalidOperationException">The resource is held or is not quarantined.</exception>
    /// <exception cref="ArgumentException">The resource name or recovery reason is empty.</exception>
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

    /// <summary>Appends one coordination event and evicts old entries while the coordinator gate is held.</summary>
    /// <param name="kind">Coordination event kind.</param>
    /// <param name="owner">Diagnostic label identifying the acquisition or session.</param>
    /// <param name="names">Formatted resource names for the event.</param>
    /// <param name="elapsed">Measured wait or ownership duration.</param>
    private void AddEvent(string kind, string owner, string names, TimeSpan elapsed)
    {
        events.Enqueue(new(DateTimeOffset.UtcNow, kind, owner, names, elapsed));
        while (events.Count > 256) events.Dequeue();
    }

    /// <summary>Finds the first quarantined resource in a normalized acquisition set.</summary>
    /// <param name="items">Normalized resource requirements involved in this operation.</param>
    /// <returns>The first quarantine exception, or null when all requested resources are available for consideration.</returns>
    private ResourceQuarantinedException? QuarantineError(ResourceRequest[] items)
    {
        foreach (var item in items)
            if (quarantine.TryGetValue(item.Name, out var reason)) return new(item.Name, reason);
        return null;
    }

    /// <summary>Marks a held resource set unsafe for reuse and rejects every affected queued request.</summary>
    /// <param name="items">Normalized resource requirements involved in this operation.</param>
    /// <param name="reason">Explanation recorded for resource quarantine.</param>
    /// <param name="owner">Diagnostic label identifying the acquisition or session.</param>
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
    /// <value>The default coordinator shared by this loaded library instance.</value>
    public static ResourceCoordinator Shared { get; } = new();

    /// <summary>Waits for and atomically acquires all requested resources.</summary>
    /// <param name="requests">Requirements; duplicates merge to the strongest access mode.</param>
    /// <param name="cancellationToken">Cancels waiting, but does not revoke a granted lease.</param>
    /// <param name="owner">Optional diagnostic label; a unique label is generated when omitted.</param>
    /// <returns>An idempotently disposable lease; retain it until cleanup is complete.</returns>
    /// <exception cref="ResourceQuarantinedException">At least one requested resource requires recovery.</exception>
    /// <exception cref="OperationCanceledException">Cancellation wins before resources are granted.</exception>
    /// <exception cref="ArgumentException">A resource name or access mode is invalid.</exception>
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
            if (owner is not null && active.Any(x => x.Owner == owner && x.Items.Any(heldItem => normalized.Any(item => item.Name == heldItem.Name))))
            {
                AddEvent("RecursiveRequest", pending.Owner, pending.Names, TimeSpan.Zero);
                throw new InvalidOperationException("The same owner cannot reacquire an already-held resource; acquire the full set together.");
            }
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

    /// <summary>Removes an acquisition if cancellation wins the race with granting its lease.</summary>
    /// <param name="pending">Queued request to cancel if it has not been granted.</param>
    /// <param name="token">Cancellation token recorded on the cancelled acquisition.</param>
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

    /// <summary>Grants complete compatible requests from the FIFO head without invoking caller code.</summary>
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

    /// <summary>Removes a granted request from ownership accounting and advances waiting acquisitions.</summary>
    /// <param name="request">Granted request whose resources must be released.</param>
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

    /// <summary>Stores normalized requirements, timing and completion state for one acquisition.</summary>
    /// <param name="items">Normalized resource requirements involved in this operation.</param>
    /// <param name="owner">Diagnostic label identifying the acquisition or session.</param>
    private sealed class Pending(ResourceRequest[] items, string owner)
    {
        /// <summary>Gets the label used to correlate this operation with diagnostic events.</summary>
        /// <value>The diagnostic owner label for this request or session.</value>
        internal string Owner { get; } = owner;
        /// <summary>Formats the resource names for human-readable coordination events.</summary>
        /// <value>Comma-separated normalized resource names.</value>
        internal string Names => string.Join(", ", Items.Select(x => x.Name));
        /// <summary>Gets the monotonic timestamp captured when the request entered acquisition.</summary>
        /// <value>A timestamp from the monotonic Stopwatch clock.</value>
        internal long Created { get; } = Stopwatch.GetTimestamp();
        /// <summary>Gets or sets the monotonic timestamp when all requested resources were granted.</summary>
        /// <value>The monotonic grant timestamp, set when ownership is assigned.</value>
        internal long Granted { get; set; }
        /// <summary>Gets the normalized resource requirements owned by this request.</summary>
        /// <value>The normalized resource requirements.</value>
        internal ResourceRequest[] Items { get; } = items;
        /// <summary>Gets or sets the queue node used to remove a cancelled pending request.</summary>
        /// <value>The queue node, whose List becomes null after removal.</value>
        internal LinkedListNode<Pending>? Node { get; set; }
        /// <summary>Gets the asynchronous completion source for the granted lease or acquisition failure.</summary>
        /// <value>A completion source yielding a lease or acquisition exception.</value>
        internal TaskCompletionSource<ResourceLease> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

}
