using NUnit.Framework;

namespace TestHarbor.Example.Comparison;

/// <summary>Models a five-volt charger, electronic load and meter shared by successive test clients.</summary>
/// <remarks>This deterministic model replaces real instruments; ownership is supplied by the calling fixture.</remarks>
public sealed class ChargerRig
{
    /// <summary>Counts connected clients independently of either fixture's locking mechanism.</summary>
    private int connected;
    /// <summary>Represents the physical electronic-load setting.</summary>
    private double loadAmps;

    /// <summary>Opens a client and rejects accidental overlapping instrument access.</summary>
    /// <returns>A connected instrument client owned by its caller.</returns>
    public ChargerClient Open()
    {
        if (Interlocked.Increment(ref connected) != 1)
        {
            Interlocked.Decrement(ref connected);
            throw new InvalidOperationException("Two clients attempted to use the charger rig together.");
        }
        TestContext.Progress.WriteLine("[Driver] Connected to simulated load and meter.");
        return new ChargerClient(this);
    }

    /// <summary>Changes the physical load, then reads charger voltage after an asynchronous settling operation.</summary>
    /// <param name="amps">Load current in amperes.</param>
    /// <param name="token">Cooperative measurement cancellation.</param>
    /// <returns>The simulated charger voltage after load-dependent droop.</returns>
    internal async Task<double> MeasureAsync(double amps, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amps);
        token.ThrowIfCancellationRequested();
        loadAmps = amps;
        await Task.Yield();
        token.ThrowIfCancellationRequested();
        // The shared load setting makes a concurrent client capable of corrupting this reading.
        var volts = 5.0 - 0.02 * loadAmps;
        TestContext.Progress.WriteLine($"[Driver] Load {amps:F2} A; measured {volts:F3} V.");
        return volts;
    }

    /// <summary>Disables the electronic load and confirms zero load before the client disconnects.</summary>
    /// <returns>A task completing after simulated load shutdown.</returns>
    internal async Task DisableLoadAsync()
    {
        await Task.Yield();
        loadAmps = 0;
        TestContext.Progress.WriteLine("[Driver] Load disabled and confirmed at zero amperes.");
    }

    /// <summary>Disconnects a client, independently rejecting release while the load remains enabled.</summary>
    internal void Disconnect()
    {
        if (loadAmps != 0) throw new InvalidOperationException("Disconnect attempted before disabling the load.");
        Interlocked.Decrement(ref connected);
        TestContext.Progress.WriteLine("[Driver] Disconnected after confirmed load shutdown.");
    }
}

/// <summary>Provides the same instrument operations to both the plain NUnit and TestHarbor examples.</summary>
/// <param name="rig">Physical simulation whose client ownership has already been granted.</param>
public sealed class ChargerClient(ChargerRig rig) : IAsyncDisposable
{
    /// <summary>Prevents repeated disposal from disconnecting the physical simulation twice.</summary>
    private bool disposed;

    /// <summary>Measures charger regulation at the requested load current.</summary>
    /// <param name="amps">Requested current in amperes.</param>
    /// <param name="token">Cooperative measurement cancellation.</param>
    /// <returns>The measured output voltage.</returns>
    public Task<double> MeasureVoltageAsync(double amps, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return rig.MeasureAsync(amps, token);
    }

    /// <summary>Confirms load shutdown and then disconnects, without using an already-cancelled test token.</summary>
    /// <returns>A task completing after instrument cleanup.</returns>
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        await rig.DisableLoadAsync();
        rig.Disconnect();
        disposed = true;
    }
}
