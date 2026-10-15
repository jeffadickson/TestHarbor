using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor.NUnit;

namespace TestHarbor.Example;

// NUnit still discovers, executes and reports these ordinary test cases.
/// <summary>Demonstrates ordinary NUnit test cases protected by a TestHarbor resource lease.</summary>
[Parallelizable(ParallelScope.All)]
public sealed class PowerSupplyTests : HarborFixture
{
    /// <summary>Declares the resource set retained throughout this fixture test and its cleanup.</summary>
    /// <value>The complete set of required resources for this test.</value>
    protected override IEnumerable<ResourceRequest> Resources => [new("DemoBench/PowerSupply")];

    /// <summary>Registers per-test services used by this fixture and its lifecycle verification.</summary>
    /// <param name="services">Collection receiving per-test dependency registrations.</param>
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<SimulatedSupply>();
        services.AddScoped<ITestExtension, SupplyOutputExtension>();
    }

    /// <summary>Enables the simulated supply during derived setup while resource ownership is active.</summary>
    [SetUp]
    public void EnableOutput() => Services.GetRequiredService<SimulatedSupply>().Enabled = true;

    /// <summary>Verifies the enabled simulated supply tracks each parameterized voltage within tolerance.</summary>
    /// <param name="volts">Requested simulated output voltage in volts.</param>
    /// <returns>A task completing when the verification and all awaited cleanup have finished.</returns>
    [TestCase(3.3)]
    [TestCase(5.0)]
    public async Task OutputTracksRequestedVoltage(double volts)
    {
        var supply = Services.GetRequiredService<SimulatedSupply>();
        await supply.SetVoltageAsync(volts);
        Assert.That(supply.Voltage, Is.EqualTo(volts).Within(0.001));
        Assert.That(supply.Enabled, Is.True);
    }

    /// <summary>Disables the simulated supply before extension and service cleanup.</summary>
    [TearDown]
    public void DisableOutput() => Services.GetRequiredService<SimulatedSupply>().Enabled = false;
}

// DI-created instruments are disposed asynchronously before resource ownership is released.
/// <summary>Provides a deterministic async supply model without connecting to real hardware.</summary>
public sealed class SimulatedSupply : IAsyncDisposable
{
    /// <summary>Gets or sets whether the simulated supply output is enabled.</summary>
    /// <value>True when simulated output is enabled.</value>
    public bool Enabled { get; set; }
    /// <summary>Gets the most recently requested simulated output voltage.</summary>
    /// <value>The most recently requested voltage.</value>
    public double Voltage { get; private set; }
    /// <summary>Updates the simulated output voltage after an asynchronous yield.</summary>
    /// <param name="volts">Requested simulated output voltage in volts.</param>
    /// <returns>A task completing after the simulated voltage is updated.</returns>
    public async Task SetVoltageAsync(double volts) { await Task.Yield(); Voltage = volts; }
    /// <summary>Disables simulated output after yielding asynchronously.</summary>
    /// <returns>A value task completing when cleanup finishes; cleanup failures propagate to the caller.</returns>
    public async ValueTask DisposeAsync() { await Task.Yield(); Enabled = false; }
}

/// <summary>Manages simulated output state around the complete NUnit per-test lifecycle.</summary>
public sealed class SupplyOutputExtension : ITestExtension
{
    /// <summary>Retains the supply resolved during startup so partial startup can be cleaned up safely.</summary>
    private SimulatedSupply? supply;

    /// <summary>Resolves the session-local supply and enables its simulated output.</summary>
    /// <param name="context">Per-test services and diagnostic owner identity, still valid during extension cleanup.</param>
    /// <param name="cancellationToken">Cooperative startup cancellation token.</param>
    /// <returns>A task completing when startup finishes, or faulting if initialization fails.</returns>
    public Task StartAsync(TestExtensionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        supply = context.Services.GetRequiredService<SimulatedSupply>();
        supply.Enabled = true;
        TestContext.Progress.WriteLine($"Extension started for {context.Owner}");
        return Task.CompletedTask;
    }

    /// <summary>Disables output, including after partial startup; the resource lease is still held.</summary>
    /// <param name="context">Per-test services and diagnostic owner identity, still valid during extension cleanup.</param>
    /// <returns>A task completing when cleanup finishes, or faulting if cleanup fails.</returns>
    public Task StopAsync(TestExtensionContext context)
    {
        if (supply is not null) supply.Enabled = false;
        TestContext.Progress.WriteLine($"Extension stopped for {context.Owner}");
        return Task.CompletedTask;
    }
}
