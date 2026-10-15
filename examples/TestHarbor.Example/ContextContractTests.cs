using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor.NUnit;

namespace TestHarbor.Example;

/// <summary>Registers shared test behavior explicitly through a declarative profile.</summary>
public sealed class TextProfile : IExtensionProfile
{
    /// <summary>Registers isolated per-test state used by contract tests.</summary>
    /// <param name="services">Session service registrations.</param>
    public void Configure(IServiceCollection services) => services.AddScoped(_ => new List<string>());
}

/// <summary>Defines reusable behavior discovered through ordinary NUnit fixture inheritance.</summary>
[UseExtensionProfile(typeof(TextProfile))]
public abstract class TextContract : HarborFixture
{
    /// <summary>Transforms text using the concrete fixture's implementation.</summary>
    /// <param name="value">Input text.</param>
    /// <returns>Transformed text preserving input length.</returns>
    protected abstract string Transform(string value);

    /// <summary>Checks a contract with explicit nested context and typed service access.</summary>
    /// <param name="input">Ordinary NUnit case data, separate from injected services.</param>
    /// <returns>A task completing after context cleanup and verification.</returns>
    [TestCase("test")]
    [TestCase("data")]
    [UseExtensionProfile(typeof(TextProfile))]
    public async Task PreservesLength(string input)
    {
        await using var context = new HarborContext("contract");
        context.Set(input);
        var child = context.Child("transformed");
        child.Set(Transform(input));
        Assert.That(child.Get<string>().Length, Is.EqualTo(context.Get<string>().Length));
        Services.GetRequiredService<List<string>>().Add(input);
        Assert.That(Services.GetRequiredService<List<string>>(), Has.Count.EqualTo(1));
    }
}

/// <summary>Runs the inherited contract against uppercase conversion.</summary>
public sealed class UppercaseContract : TextContract
{
    /// <summary>Converts text using invariant uppercase rules.</summary>
    /// <param name="value">Input text.</param>
    /// <returns>Uppercase text.</returns>
    protected override string Transform(string value) => value.ToUpperInvariant();
}

/// <summary>Runs the inherited contract against unchanged text.</summary>
public sealed class IdentityContract : TextContract
{
    /// <summary>Returns the input text unchanged.</summary>
    /// <param name="value">Input text.</param>
    /// <returns>The same string.</returns>
    protected override string Transform(string value) => value;
}

/// <summary>Demonstrates parameterized fixture contexts without replacing NUnit fixture construction.</summary>
/// <param name="scale">Immutable fixture data supplied during discovery.</param>
[TestFixture(2)]
[TestFixture(3)]
public sealed class ParameterizedContext(int scale) : HarborFixture
{
    /// <summary>Uses fixture data inside an ordinary case with its own service scope.</summary>
    /// <param name="input">Method-level case data.</param>
    [TestCase(4)]
    public void ScalesInput(int input) => Assert.That(input * scale, Is.GreaterThan(input));
}
