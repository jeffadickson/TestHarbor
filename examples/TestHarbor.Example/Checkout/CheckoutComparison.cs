using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor.NUnit.Injection;

namespace TestHarbor.Example.Checkout;

/// <summary>Holds one test's stock and completed order totals in cents.</summary>
public sealed class CheckoutStore
{
    /// <summary>Gets or sets the available units of the example product.</summary>
    /// <value>Five units initially; reduced only by accepted orders.</value>
    public int Stock { get; set; } = 5;

    /// <summary>Gets the recorded totals of accepted orders.</summary>
    /// <value>An initially empty collection belonging to one test case.</value>
    public List<int> Orders { get; } = [];
}

/// <summary>Calculates a ten percent quantity discount without floating-point arithmetic.</summary>
public sealed class CheckoutPricing
{
    /// <summary>Calculates the price for an already validated quantity.</summary>
    /// <param name="quantity">Positive number of units.</param>
    /// <returns>Total cents at 1000 per unit, or 900 when buying at least three.</returns>
    public int Total(int quantity) => quantity * (quantity >= 3 ? 900 : 1000);
}

/// <summary>Validates checkout before changing stock or recording an order.</summary>
/// <param name="store">Attempt-local mutable repository.</param>
/// <param name="pricing">Stateless pricing dependency.</param>
public sealed class CheckoutService(CheckoutStore store, CheckoutPricing pricing)
{
    /// <summary>Attempts to purchase the example product.</summary>
    /// <param name="quantity">Requested number of units.</param>
    /// <returns>Whether an order was accepted and recorded.</returns>
    public bool Submit(int quantity)
    {
        if (quantity <= 0 || quantity > store.Stock) return false;
        var total = pricing.Total(quantity);
        store.Stock -= quantity;
        store.Orders.Add(total);
        return true;
    }
}

/// <summary>Declares the test's dependency graph once, without constructing it in each fixture.</summary>
public sealed class CheckoutProfile : InjectionProfile
{
    /// <summary>Registers the repository, pricing policy and service for each attempt.</summary>
    /// <param name="services">Fresh attempt's registration collection.</param>
    public override void Configure(IServiceCollection services)
    {
        services.AddScoped<CheckoutStore>();
        services.AddScoped<CheckoutPricing>();
        services.AddScoped<CheckoutService>();
    }
}

/// <summary>Shares business assertions so both fixture styles must establish identical behavior.</summary>
public static class CheckoutExpectations
{
    /// <summary>Checks case isolation, pricing and absence of side effects on rejection.</summary>
    /// <param name="checkout">Service under test.</param>
    /// <param name="store">The repository used inside the service.</param>
    /// <param name="quantity">Ordinary NUnit case input.</param>
    /// <param name="accepted">Expected submission result.</param>
    /// <param name="total">Expected accepted total in cents, or zero for rejection.</param>
    public static void Verify(CheckoutService checkout, CheckoutStore store, int quantity, bool accepted, int total)
    {
        Assert.That(store.Stock, Is.EqualTo(5), "Each case needs its own initial stock.");
        Assert.That(store.Orders, Is.Empty, "Orders must not cross test-case boundaries.");
        Assert.That(checkout.Submit(quantity), Is.EqualTo(accepted));
        Assert.That(store.Stock, Is.EqualTo(accepted ? 5 - quantity : 5));
        Assert.That(store.Orders, Is.EqualTo(accepted ? new[] { total } : Array.Empty<int>()));
    }
}

/// <summary>Shows explicit construction of the same graph with ordinary NUnit.</summary>
[FixtureLifeCycle(LifeCycle.InstancePerTestCase), Parallelizable(ParallelScope.All)]
public sealed class PlainNUnitCheckoutTests
{
    /// <summary>Retains the repository explicitly constructed by setup.</summary>
    private CheckoutStore store = null!;

    /// <summary>Retains the application service explicitly constructed by setup.</summary>
    private CheckoutService checkout = null!;

    /// <summary>Wires the repository and pricing policy into the service for each case.</summary>
    [SetUp]
    public void SetUp()
    {
        store = new CheckoutStore();
        checkout = new CheckoutService(store, new CheckoutPricing());
    }

    /// <summary>Runs ordinary case data against the manually assembled graph.</summary>
    /// <param name="quantity">Requested units.</param>
    /// <param name="accepted">Expected acceptance.</param>
    /// <param name="total">Expected total cents.</param>
    [TestCase(1, true, 1000), TestCase(3, true, 2700), TestCase(0, false, 0), TestCase(6, false, 0)]
    public void Checkout(int quantity, bool accepted, int total)
    {
        CheckoutExpectations.Verify(checkout, store, quantity, accepted, total);
        TestContext.Out.WriteLine("Plain NUnit: setup explicitly creates and connects the graph; NUnit runs the cases and assertions.");
    }
}

/// <summary>Receives a transitively constructed graph and requests its repository only in the test that needs it.</summary>
/// <param name="checkout">Application service constructed from the profile's registrations.</param>
[HarborInjectedFixture(typeof(CheckoutProfile)), Parallelizable(ParallelScope.All)]
public sealed class InjectedCheckoutTests(CheckoutService checkout)
{
    /// <summary>Combines three ordinary data parameters with a repository from the constructor's scope.</summary>
    /// <param name="quantity">Requested units.</param>
    /// <param name="store">The same repository already injected into CheckoutService.</param>
    /// <param name="accepted">Expected acceptance.</param>
    /// <param name="total">Expected total cents.</param>
    [TestCase(1, true, 1000), TestCase(3, true, 2700), TestCase(0, false, 0), TestCase(6, false, 0)]
    public void Checkout(int quantity, [FromServices] CheckoutStore store, bool accepted, int total)
    {
        CheckoutExpectations.Verify(checkout, store, quantity, accepted, total);
        TestContext.Out.WriteLine("TestHarbor: the profile constructs the transitive graph before the fixture constructor. Method injection observes that same scoped repository; parallel cases have separate stock. NUnit still supplies case data, scheduling and assertions.");
    }
}
