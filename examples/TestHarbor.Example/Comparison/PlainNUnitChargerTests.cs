using NUnit.Framework;

namespace TestHarbor.Example.Comparison;

/// <summary>Shows a correct manual lock and async cleanup around the charger measurement using plain NUnit.</summary>
/// <remarks>The semaphore is local to this fixture and is not a general named-resource or quarantine system.</remarks>
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[Parallelizable(ParallelScope.All)]
public sealed class PlainNUnitChargerTests
{
    /// <summary>Represents the shared physical equipment used by this version of the example.</summary>
    private static readonly ChargerRig Rig = new();
    /// <summary>Serializes callers that explicitly agree to use this fixture's semaphore.</summary>
    private static readonly SemaphoreSlim RigGate = new(1, 1);
    /// <summary>Retains this test's manually owned instrument client.</summary>
    private ChargerClient? client;
    /// <summary>Records whether setup acquired the semaphore so cleanup cannot release another owner's permit.</summary>
    private bool acquired;

    /// <summary>Manually acquires the rig before opening the client, rolling back acquisition if opening fails.</summary>
    /// <returns>A task completing after the client is available.</returns>
    [SetUp]
    public async Task Connect()
    {
        await RigGate.WaitAsync(TestContext.CurrentContext.CancellationToken);
        acquired = true;
        try { client = Rig.Open(); }
        catch
        {
            RigGate.Release();
            acquired = false;
            throw;
        }
    }

    /// <summary>Checks the charger stays within its five-volt tolerance under load.</summary>
    /// <param name="amps">Electronic-load current in amperes.</param>
    /// <returns>A task completing after the voltage assertion.</returns>
    [TestCase(0.1)]
    [TestCase(0.5)]
    [TestCase(1.0)]
    public async Task StaysWithinFiveVoltTolerance(double amps)
    {
        var volts = await client!.MeasureVoltageAsync(amps, TestContext.CurrentContext.CancellationToken);
        Assert.That(volts, Is.InRange(4.75, 5.25));
        TestContext.Out.WriteLine("[Plain NUnit] Same measurement and assertion. This fixture owns its semaphore, startup rollback, client lifetime and cleanup ordering.");
    }

    /// <summary>Awaits client shutdown before releasing the manually managed semaphore.</summary>
    /// <remarks>Cleanup errors fail the test, but this minimal fixture does not quarantine equipment.</remarks>
    /// <returns>A task completing after cleanup and semaphore release.</returns>
    [TearDown]
    public async Task Disconnect()
    {
        try
        {
            if (client is not null) await client.DisposeAsync();
        }
        finally
        {
            if (acquired)
            {
                acquired = false;
                RigGate.Release();
            }
        }
    }
}
