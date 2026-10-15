using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor.NUnit;

namespace TestHarbor.Example.Comparison;

/// <summary>Checks the same charger requirement while declaring reusable bench ownership and client registration.</summary>
[RequiresResource("ChargerBench/Load")]
[RequiresResource("ChargerBench/Meter")]
[UseExtensionProfile(typeof(ChargerProfile))]
[Parallelizable(ParallelScope.All)]
public sealed class TestHarborChargerTests : HarborFixture
{
    /// <summary>Checks the charger stays within its five-volt tolerance under load.</summary>
    /// <param name="amps">Electronic-load current in amperes.</param>
    /// <returns>A task completing after the voltage assertion.</returns>
    [TestCase(0.1)]
    [TestCase(0.5)]
    [TestCase(1.0)]
    public async Task StaysWithinFiveVoltTolerance(double amps)
    {
        var client = Services.GetRequiredService<ChargerClient>();
        var volts = await client.MeasureVoltageAsync(amps, TestContext.CurrentContext.CancellationToken);
        Assert.That(volts, Is.InRange(4.75, 5.25));
        TestContext.Out.WriteLine("[TestHarbor] Same measurement and assertion. Named leases cover setup through awaited disposal; the shared profile supplies a fresh client; cleanup failure quarantines both resources.");
    }
}

/// <summary>Registers the charger client once for reuse by TestHarbor fixtures using this bench.</summary>
/// <remarks>Registration does not open equipment. The scoped factory runs when the client is resolved under a lease.</remarks>
public sealed class ChargerProfile : IExtensionProfile
{
    /// <summary>Represents physical equipment shared by this example's TestHarbor clients.</summary>
    private static readonly ChargerRig Rig = new();

    /// <summary>Registers a per-test client whose async disposal is owned by the session's DI scope.</summary>
    /// <param name="services">Session-local registrations.</param>
    public void Configure(IServiceCollection services) => services.AddScoped(_ => Rig.Open());
}
