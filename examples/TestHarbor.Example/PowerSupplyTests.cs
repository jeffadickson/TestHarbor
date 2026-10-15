using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor.NUnit;

namespace TestHarbor.Example;

// NUnit still discovers, executes and reports these ordinary test cases.
[Parallelizable(ParallelScope.All)]
public sealed class PowerSupplyTests : HarborFixture
{
    protected override IEnumerable<ResourceRequest> Resources => [new("DemoBench/PowerSupply")];

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<SimulatedSupply>();
        services.AddScoped<ITestExtension, SupplyOutputExtension>();
    }

    [SetUp]
    public void EnableOutput() => Services.GetRequiredService<SimulatedSupply>().Enabled = true;

    [TestCase(3.3)]
    [TestCase(5.0)]
    public async Task OutputTracksRequestedVoltage(double volts)
    {
        var supply = Services.GetRequiredService<SimulatedSupply>();
        await supply.SetVoltageAsync(volts);
        Assert.That(supply.Voltage, Is.EqualTo(volts).Within(0.001));
        Assert.That(supply.Enabled, Is.True);
    }

    [TearDown]
    public void DisableOutput() => Services.GetRequiredService<SimulatedSupply>().Enabled = false;
}

// DI-created instruments are disposed asynchronously before resource ownership is released.
public sealed class SimulatedSupply : IAsyncDisposable
{
    public bool Enabled { get; set; }
    public double Voltage { get; private set; }
    public async Task SetVoltageAsync(double volts) { await Task.Yield(); Voltage = volts; }
    public async ValueTask DisposeAsync() { await Task.Yield(); Enabled = false; }
}

/// <summary>Manages simulated output state around the complete NUnit per-test lifecycle.</summary>
public sealed class SupplyOutputExtension : ITestExtension
{
    private SimulatedSupply? supply;

    /// <summary>Resolves the session-local supply and enables its simulated output.</summary>
    public Task StartAsync(TestExtensionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        supply = context.Services.GetRequiredService<SimulatedSupply>();
        supply.Enabled = true;
        TestContext.Progress.WriteLine($"Extension started for {context.Owner}");
        return Task.CompletedTask;
    }

    /// <summary>Disables output, including after partial startup; the resource lease is still held.</summary>
    public Task StopAsync(TestExtensionContext context)
    {
        if (supply is not null) supply.Enabled = false;
        TestContext.Progress.WriteLine($"Extension stopped for {context.Owner}");
        return Task.CompletedTask;
    }
}
