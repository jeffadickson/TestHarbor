using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor.NUnit;

namespace TestHarbor.Example.Software;

/// <summary>Demonstrates per-test application services without acquiring any shared resource.</summary>
[UseExtensionProfile(typeof(ShoppingProfile))]
[Parallelizable(ParallelScope.All)]
public sealed class ShoppingCartTests : HarborFixture
{
    /// <summary>Checks each parameterized case receives an empty cart and applies the requested discount.</summary>
    /// <param name="percent">Discount percentage applied to the cart.</param>
    /// <param name="expected">Expected discounted total.</param>
    [TestCase(0, 30)]
    [TestCase(10, 27)]
    public void CalculatesDiscountWithFreshCart(int percent, int expected)
    {
        var cart = Services.GetRequiredService<ShoppingCart>();
        Assert.That(cart.Count, Is.Zero, "Another case's items must not leak into this case.");
        cart.Add(10m);
        cart.Add(20m);
        Assert.That(cart.Total(percent), Is.EqualTo((decimal)expected));
        TestContext.Out.WriteLine("[Software/DI] NUnit supplies cases and assertions. A reusable TestHarbor profile creates isolated services per case, so parallel cases do not share cart state. No resource lock is needed.");
    }
}

/// <summary>Registers an application's test services once for reuse across fixtures.</summary>
public sealed class ShoppingProfile : IExtensionProfile
{
    /// <summary>Registers a fresh mutable shopping cart for each session.</summary>
    /// <param name="services">Session-local service registrations.</param>
    public void Configure(IServiceCollection services) => services.AddScoped<ShoppingCart>();
}

/// <summary>Provides a small application component with mutable per-request state.</summary>
public sealed class ShoppingCart
{
    /// <summary>Stores line-item prices owned by this cart.</summary>
    private readonly List<decimal> prices = [];
    /// <summary>Gets the number of line items.</summary>
    /// <value>The current cart's item count.</value>
    public int Count => prices.Count;

    /// <summary>Adds a nonnegative item price.</summary>
    /// <param name="price">Item price in the cart's currency.</param>
    public void Add(decimal price)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(price);
        prices.Add(price);
    }

    /// <summary>Calculates the discounted total using decimal arithmetic.</summary>
    /// <param name="percent">Discount percentage from zero through one hundred.</param>
    /// <returns>The discounted sum of line-item prices.</returns>
    public decimal Total(int percent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(percent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100);
        return prices.Sum() * (100 - percent) / 100m;
    }
}
