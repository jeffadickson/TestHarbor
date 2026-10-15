using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TestHarbor;

/// <summary>Coordinates exclusive resource sets across cooperating .NET processes on one host.</summary>
/// <remarks>Use one trusted local directory on a filesystem honoring exclusive opens and atomic rename.
/// Network filesystems and multi-host distribution are unsupported. Never delete broker files to recover
/// equipment. An abandoned active record requires explicit recovery; heartbeat age never revokes ownership.</remarks>
public sealed class FileResourceBroker
{
    /// <summary>Stores the broker's absolute trusted state directory.</summary>
    private readonly string root;

    /// <summary>Creates or opens a broker directory shared by cooperating processes.</summary>
    /// <param name="directory">Trusted local directory whose permissions define recovery authority.</param>
    public FileResourceBroker(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        root = Path.GetFullPath(directory);
        Directory.CreateDirectory(root);
    }

    /// <summary>Acquires an entire exclusive resource set, releasing partial locks between retries.</summary>
    /// <param name="resources">Case-sensitive resource names; duplicates are removed.</param>
    /// <param name="owner">Non-secret owner identity persisted for diagnosis.</param>
    /// <param name="cancellationToken">Cancels waiting without revoking another owner.</param>
    /// <param name="heartbeatTimeout">Optional liveness deadline; expiry quarantines but never releases ownership.</param>
    /// <returns>A fenced lease retaining OS-backed exclusive handles until disposal.</returns>
    public async Task<FileResourceLease> AcquireAsync(IEnumerable<string> resources, string owner,
        CancellationToken cancellationToken = default, TimeSpan? heartbeatTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (heartbeatTimeout is { } timeout && (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1))
            throw new ArgumentOutOfRangeException(nameof(heartbeatTimeout));
        var names = resources.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        foreach (var name in names) ArgumentException.ThrowIfNullOrWhiteSpace(name);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handles = new List<BrokerHandle>();
            var transferred = false;
            try
            {
                foreach (var name in names)
                {
                    var handle = TryOpen(name);
                    if (handle is null) break;
                    handles.Add(handle);
                }
                if (handles.Count == names.Length)
                {
                    foreach (var handle in handles)
                    {
                        var state = Read(handle);
                        if (state.Active || state.Quarantine is not null)
                            throw new ResourceQuarantinedException(state.Name, state.Quarantine ?? "Previous owner exited without completing lease cleanup.");
                    }
                    foreach (var handle in handles)
                    {
                        var old = Read(handle);
                        Write(handle, old with { Generation = checked(old.Generation + 1), Owner = owner,
                            Heartbeat = DateTimeOffset.UtcNow, Active = true });
                    }
                    var lease = new FileResourceLease(handles, heartbeatTimeout);
                    transferred = true;
                    return lease;
                }
            }
            finally { if (!transferred) foreach (var handle in handles) handle.Lock.Dispose(); }
            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Clears abandoned ownership or quarantine after caller-verified physical recovery.</summary>
    /// <param name="resource">Resource to recover.</param>
    /// <param name="reason">Nonempty persistent recovery explanation.</param>
    /// <exception cref="InvalidOperationException">Another process still owns the resource.</exception>
    public void Recover(string resource, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var handle = TryOpen(resource) ?? throw new InvalidOperationException("Cannot recover an actively held resource.");
        using (handle.Lock)
        {
            var state = Read(handle);
            if (!state.Active && state.Quarantine is null) throw new InvalidOperationException("Resource does not require recovery.");
            Write(handle, state with { Active = false, Quarantine = null, Owner = "recovery: " + reason, Heartbeat = DateTimeOffset.UtcNow });
        }
    }

    /// <summary>Attempts an exclusive OS-backed open without treating malformed state as availability.</summary>
    /// <param name="name">Logical resource name.</param>
    /// <returns>A locked handle, or null when another process holds the lock file.</returns>
    private BrokerHandle? TryOpen(string name)
    {
        var stem = Path.Combine(root, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name))));
        FileStream file;
        try { file = new FileStream(stem + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        // Unix IOException carries errno: EWOULDBLOCK is 11 on Linux and 35 on macOS.
        catch (IOException error) when ((error.HResult & 0xffff) is 11 or 32 or 33
            || (OperatingSystem.IsMacOS() && (error.HResult & 0xffff) == 35)) { return null; }
        return new(name, stem + ".json", file);
    }

    /// <summary>Reads state while holding the corresponding exclusive lock.</summary>
    /// <param name="handle">Locked resource handle.</param>
    /// <returns>Validated persistent state, or the initial state for a new resource.</returns>
    internal static BrokerState Read(BrokerHandle handle)
    {
        if (!File.Exists(handle.StatePath)) return new(handle.Name, 0, "", DateTimeOffset.MinValue, false, null);
        var state = JsonSerializer.Deserialize<BrokerState>(File.ReadAllText(handle.StatePath))
            ?? throw new IOException("Invalid broker state; operator investigation required.");
        if (state.Name != handle.Name || state.Generation < 0) throw new IOException("Broker identity mismatch; refusing acquisition.");
        return state;
    }

    /// <summary>Flushes a replacement record before atomically publishing it.</summary>
    /// <param name="handle">Locked resource handle.</param>
    /// <param name="state">Replacement state with a nondecreasing generation.</param>
    internal static void Write(BrokerHandle handle, BrokerState state)
    {
        var temporary = handle.StatePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, state); stream.Flush(true); }
            File.Move(temporary, handle.StatePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

/// <summary>Retains cross-process ownership and monotonic fencing generations for an exclusive set.</summary>
/// <remarks>Downstream equipment gateways must enforce fencing tokens if they accept delayed commands.
/// A token alone cannot stop hardware. Optional heartbeat expiry quarantines ownership without releasing it.</remarks>
public sealed class FileResourceLease : IDisposable
{
    /// <summary>Stores the optional heartbeat deadline.</summary>
    private readonly TimeSpan? heartbeatTimeout;
    /// <summary>Runs the optional expiry check without releasing ownership.</summary>
    private readonly Timer? heartbeatTimer;
    /// <summary>Records monotonic time of the last successful heartbeat.</summary>
    private long heartbeatTick = Stopwatch.GetTimestamp();
    /// <summary>Remembers a liveness expiry until explicit operator recovery after release.</summary>
    private bool expired;
    /// <summary>Retains errors encountered while persisting expiry evidence.</summary>
    private Exception? expiryError;
    /// <summary>Serializes heartbeat, quarantine and release updates.</summary>
    private readonly object gate = new();
    /// <summary>Retains exclusive handles for every resource in the set.</summary>
    private readonly IReadOnlyList<BrokerHandle> handles;
    /// <summary>Prevents updates after ownership has been released.</summary>
    private bool disposed;
    /// <summary>Gets the fencing generation for each acquired resource.</summary>
    /// <value>An immutable mapping whose generations increase on every successful acquisition.</value>
    public IReadOnlyDictionary<string, long> Tokens { get; }

    /// <summary>Binds persistent active records to their still-held lock handles.</summary>
    /// <param name="handles">Handles whose ownership transfers to this lease.</param>
    /// <param name="heartbeatTimeout">Optional liveness deadline that quarantines expired ownership.</param>
    internal FileResourceLease(IReadOnlyList<BrokerHandle> handles, TimeSpan? heartbeatTimeout)
    {
        this.handles = handles;
        this.heartbeatTimeout = heartbeatTimeout;
        Tokens = new ReadOnlyDictionary<string, long>(handles.ToDictionary(x => x.Name, x => FileResourceBroker.Read(x).Generation, StringComparer.Ordinal));
        heartbeatTick = Stopwatch.GetTimestamp();
        if (heartbeatTimeout is { } timeout)
            heartbeatTimer = new Timer(_ => CheckExpiry(), null, timeout, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Gets whether the configured heartbeat deadline has expired.</summary>
    /// <value>True once expired; expiry never releases active ownership.</value>
    public bool IsExpired
    {
        get { lock (gate) { CheckExpiry(); return expired; } }
    }

    /// <summary>Quarantines overdue ownership and retains persistence failures for disposal.</summary>
    private void CheckExpiry()
    {
        lock (gate)
        {
            if (disposed || expired || heartbeatTimeout is not { } timeout) return;
            var elapsed = Stopwatch.GetElapsedTime(heartbeatTick);
            if (elapsed < timeout)
            {
                heartbeatTimer?.Change(timeout - elapsed, Timeout.InfiniteTimeSpan);
                return;
            }
            expired = true;
            try
            {
                foreach (var handle in handles)
                    FileResourceBroker.Write(handle, FileResourceBroker.Read(handle) with { Quarantine = "Heartbeat deadline expired; physical recovery required." });
            }
            catch (Exception error) { expiryError = error; }
        }
    }

    /// <summary>Updates persisted liveness timestamps without extending or revoking ownership.</summary>
    public void Heartbeat()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            CheckExpiry();
            if (expired) throw new InvalidOperationException("Expired ownership cannot be renewed; verify physical recovery after release.");
            foreach (var handle in handles)
                FileResourceBroker.Write(handle, FileResourceBroker.Read(handle) with { Heartbeat = DateTimeOffset.UtcNow });
            heartbeatTick = Stopwatch.GetTimestamp();
            if (heartbeatTimeout is { } timeout) heartbeatTimer!.Change(timeout, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Persistently blocks reuse while preserving current ownership.</summary>
    /// <param name="reason">Nonempty recovery explanation.</param>
    public void Quarantine(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            foreach (var handle in handles)
                FileResourceBroker.Write(handle, FileResourceBroker.Read(handle) with { Quarantine = reason });
        }
    }

    /// <summary>Persists completed ownership and releases every handle, preserving all write failures.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            CheckExpiry();
            disposed = true;
            heartbeatTimer?.Dispose();
            List<Exception> errors = [];
            if (expiryError is not null) errors.Add(expiryError);
            foreach (var handle in handles.Reverse())
            {
                try
                {
                    var state = FileResourceBroker.Read(handle);
                    FileResourceBroker.Write(handle, state with { Active = false,
                        Quarantine = expired ? state.Quarantine ?? "Heartbeat deadline expired; recovery required." : state.Quarantine });
                }
                catch (Exception error) { errors.Add(error); }
                finally { handle.Lock.Dispose(); }
            }
            if (errors.Count != 0) throw new AggregateException("Broker release failed; retained active records require recovery.", errors);
        }
    }
}

/// <summary>Pairs resource identity and persistent state with its exclusive open handle.</summary>
/// <param name="Name">Logical resource name.</param>
/// <param name="StatePath">Persistent record path.</param>
/// <param name="Lock">Exclusive open lock file.</param>
internal sealed record BrokerHandle(string Name, string StatePath, FileStream Lock);

/// <summary>Stores durable ownership evidence and the last fencing generation.</summary>
/// <param name="Name">Logical resource identity.</param>
/// <param name="Generation">Last monotonically increasing fencing token.</param>
/// <param name="Owner">Owner or recovery authority explanation.</param>
/// <param name="Heartbeat">Last diagnostic liveness update.</param>
/// <param name="Active">Whether cleanup has not yet completed.</param>
/// <param name="Quarantine">Explicit failure reason, if quarantined.</param>
internal sealed record BrokerState(string Name, long Generation, string Owner, DateTimeOffset Heartbeat, bool Active, string? Quarantine);
