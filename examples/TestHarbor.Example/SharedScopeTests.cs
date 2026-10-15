using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor.NUnit;

namespace TestHarbor.Example;

/// <summary>Demonstrates an explicit fixture owner initialized only when this fixture is selected.</summary>
[Parallelizable(ParallelScope.All)]
public sealed class SharedScopeTests : HarborFixture
{
    /// <summary>Owns shared services and the fixture-lifetime equipment lease.</summary>
    private static TestServiceScope? owner;
    /// <summary>Creates the fixture provider and holds its resource through final singleton cleanup.</summary>
    /// <returns>A task completing when fixture resources are owned.</returns>
    [OneTimeSetUp]
    public static async Task OpenOwner() => owner = await TestServiceScope.OpenAsync([new("Example/Shared")], services =>
    {
        services.AddSingleton(new object());
        services.AddScoped(_ => new List<int>());
    });
    /// <summary>Supplies the explicitly owned fixture provider to each session.</summary>
    /// <value>Options using the fixture owner without overriding its registrations.</value>
    protected override TestSessionOptions SessionOptions => new() { ServiceScope = owner ?? throw new InvalidOperationException("Owner not initialized.") };
    /// <summary>Verifies parallel cases have isolated scoped state while using the fixture resource.</summary>
    /// <param name="value">Case value stored in isolated state.</param>
    [TestCase(1)]
    [TestCase(2)]
    public void IsolatedChildScope(int value)
    {
        var values = Services.GetRequiredService<List<int>>();
        Assert.That(values, Is.Empty);
        values.Add(value);
        Assert.That(ResourceCoordinator.Shared.GetSnapshot().Single(x => x.Name == "Example/Shared").Exclusive, Is.True);
    }
    /// <summary>Disposes shared services and releases fixture ownership after all child sessions finish.</summary>
    /// <returns>A task completing after scope and lease cleanup.</returns>
    [OneTimeTearDown]
    public static async Task CloseOwner()
    {
        if (owner is not null) await owner.DisposeAsync();
        owner = null;
    }
}
