using TestHarbor;

namespace TestHarbor.BrokerProbe;

/// <summary>Runs isolated broker clients for cross-process conformance verification.</summary>
internal static class Program
{
    /// <summary>Acquires, holds, recovers or quarantines a test resource.</summary>
    /// <param name="args">Broker directory followed by hold, acquire, quarantine or recover.</param>
    /// <returns>Zero for success, two for blocked acquisition, or three for quarantine.</returns>
    private static async Task<int> Main(string[] args)
    {
        var broker = new FileResourceBroker(args[0]);
        if (args[1] == "recover") { broker.Recover("device", "Verified simulated recovery"); return 0; }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            using var lease = await broker.AcquireAsync(["device"], Environment.ProcessId.ToString(), timeout.Token,
                args[1] == "expire" ? TimeSpan.FromMilliseconds(30) : null);
            if (args[1] != "expire") lease.Heartbeat();
            Console.WriteLine(lease.Tokens["device"]);
            Console.Out.Flush();
            if (args[1] == "expire")
            {
                await Task.Delay(100);
                if (!lease.IsExpired) return 4;
                using var contenderTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                try { using var contender = await broker.AcquireAsync(["device"], "contender", contenderTimeout.Token); return 5; }
                catch (OperationCanceledException) { }
            }
            if (args[1] == "hold") await Task.Delay(Timeout.Infinite);
            if (args[1] == "quarantine") lease.Quarantine("Simulated cleanup failure");
            return 0;
        }
        catch (OperationCanceledException) { return 2; }
        catch (ResourceQuarantinedException) { return 3; }
    }
}
