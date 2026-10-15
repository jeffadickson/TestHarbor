using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor.NUnit;

namespace TestHarbor.Example.Software;

/// <summary>Shows ownership and reset policies for a shared software integration-test environment.</summary>
/// <remarks>The catalog is an in-memory database stand-in, not an actual database driver or transaction test.</remarks>
[RequiresResource("IntegrationEnvironment/Catalog")]
[UseExtensionProfile(typeof(CatalogProfile))]
[Parallelizable(ParallelScope.All)]
public sealed class SharedCatalogTests : HarborFixture
{
    /// <summary>Verifies each case begins with a reset environment and reads its own inserted record.</summary>
    /// <param name="product">Product identifier inserted by this case.</param>
    /// <returns>A task completing after insertion and lookup.</returns>
    [TestCase("book")]
    [TestCase("pencil")]
    public async Task InsertsIntoCleanCatalog(string product)
    {
        var catalog = Services.GetRequiredService<SharedCatalog>();
        Assert.That(catalog.Count, Is.Zero, "A previous test must not leave records behind.");
        catalog.Add(product, 3);
        await Task.Yield();
        Assert.That(catalog.Quantity(product), Is.EqualTo(3));
        Assert.That(catalog.Count, Is.EqualTo(1));
        TestContext.Out.WriteLine("[Software/shared state] TestHarbor owns the named catalog through reset and test execution. The extension supplies reset operations; NUnit checks the records. Other tests using this name cannot reset the catalog during this test.");
    }
}

/// <summary>Registers the shared catalog and its reusable environment-reset extension.</summary>
public sealed class CatalogProfile : IExtensionProfile
{
    /// <summary>Represents one shared integration-test database for this process.</summary>
    private static readonly SharedCatalog Catalog = new();

    /// <summary>Registers shared external state and a per-session reset extension.</summary>
    /// <param name="services">Session-local service registrations.</param>
    public void Configure(IServiceCollection services)
    {
        services.AddSingleton(Catalog);
        services.AddScoped<ITestExtension, CatalogReset>();
    }
}

/// <summary>Models shared mutable catalog records without requiring a database server.</summary>
public sealed class SharedCatalog
{
    /// <summary>Holds records shared by cooperating test sessions.</summary>
    private readonly ConcurrentDictionary<string, int> quantities = new();
    /// <summary>Gets the number of catalog records.</summary>
    /// <value>The current number of product entries.</value>
    public int Count => quantities.Count;

    /// <summary>Adds a product and fails on duplicate identifiers.</summary>
    /// <param name="product">Product identifier.</param>
    /// <param name="quantity">Available stock count.</param>
    public void Add(string product, int quantity)
    {
        if (!quantities.TryAdd(product, quantity)) throw new InvalidOperationException("Product already exists.");
    }

    /// <summary>Looks up a product's stock count.</summary>
    /// <param name="product">Product identifier.</param>
    /// <returns>The stored stock count.</returns>
    public int Quantity(string product) => quantities[product];

    /// <summary>Resets the test environment's records using an asynchronous operation.</summary>
    /// <returns>A task completing after all test data is removed.</returns>
    public async Task ResetAsync()
    {
        await Task.Yield();
        quantities.Clear();
        TestContext.Progress.WriteLine("[Software/catalog] Shared test data reset while the catalog lease is held.");
    }
}

/// <summary>Defines environment-specific reset behavior invoked under TestHarbor ownership.</summary>
/// <param name="catalog">Shared test catalog.</param>
public sealed class CatalogReset(SharedCatalog catalog) : ITestExtension
{
    /// <summary>Resets the environment before the test runs.</summary>
    /// <param name="context">Session services and diagnostic owner.</param>
    /// <param name="cancellationToken">Cooperative startup cancellation.</param>
    /// <returns>A task completing when the environment is clean.</returns>
    public Task StartAsync(TestExtensionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return catalog.ResetAsync();
    }

    /// <summary>Resets the environment after the test without inheriting a cancelled test token.</summary>
    /// <param name="context">Session services and diagnostic owner.</param>
    /// <returns>A task completing when cleanup is confirmed.</returns>
    public Task StopAsync(TestExtensionContext context) => catalog.ResetAsync();
}
