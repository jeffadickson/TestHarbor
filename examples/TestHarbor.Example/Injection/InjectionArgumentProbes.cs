using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor.NUnit.Injection;

namespace TestHarbor.Example.Injection;

/// <summary>Fails immediately if an ignored test activates its registration profile.</summary>
public sealed class IgnoredActivationProfile : InjectionProfile
{
    /// <summary>Reports unexpected profile construction before an ignored case.</summary>
    public IgnoredActivationProfile() => throw new InvalidOperationException("ignored-profile-activated");

    /// <summary>Fails if registration is attempted for an ignored case.</summary>
    /// <param name="services">Unused registration collection.</param>
    public override void Configure(IServiceCollection services) => throw new InvalidOperationException("ignored-registration-activated");
}

/// <summary>Checks NUnit ignore decisions precede TestHarbor runtime activation.</summary>
[HarborInjectedFixture(typeof(IgnoredActivationProfile))]
public sealed class InjectionIgnoredTests
{
    /// <summary>Must never activate a profile or invoke its body.</summary>
    /// <param name="service">Service that must never resolve.</param>
    [Test, Ignore("Intentional activation guard")]
    public void IgnoredMethod([FromServices] KeyedEndpoint service) => Assert.Fail("unreachable-body");

    /// <summary>Gets a case marked ignored by its source.</summary>
    /// <value>One ignored source row.</value>
    public static IEnumerable<TestCaseData> Rows => [new TestCaseData(1).Ignore("Intentional source activation guard")];

    /// <summary>Must honor the source's ignore metadata before dependency resolution.</summary>
    /// <param name="input">Ordinary source data.</param>
    /// <param name="service">Service that must never resolve.</param>
    [TestCaseSource(nameof(Rows))]
    public void IgnoredSource(int input, [FromServices] KeyedEndpoint service) => Assert.Fail("unreachable-body");
}

/// <summary>Preserves unsupported service signature guards while allowing optional and params data.</summary>
[HarborInjectedFixture(typeof(KeyedProfile)), Explicit("Run argument compatibility verifier")]
public sealed class InjectionArgumentInvalid
{
    /// <summary>Rejects optional service parameters rather than using a null fallback.</summary>
    /// <param name="service">Unsupported optional dependency.</param>
    [Test]
    public void OptionalService([FromServices] KeyedEndpoint? service = null) => Assert.Fail("unreachable-body");

    /// <summary>Rejects params service arrays rather than treating them as ordinary case data.</summary>
    /// <param name="services">Unsupported params dependency.</param>
    [Test]
    public void ParamsService([FromServices] params KeyedEndpoint[] services) => Assert.Fail("unreachable-body");

    /// <summary>Rejects ref data parameters even after ordinary argument support is expanded.</summary>
    /// <param name="input">Unsupported by-reference case data.</param>
    /// <param name="service">Ordinary injected dependency.</param>
    [TestCase(1)]
    public void RefData(ref int input, [FromServices] KeyedEndpoint service) => Assert.Fail("unreachable-body");

    /// <summary>Does not silently supply the unkeyed provider for an explicit missing key.</summary>
    /// <param name="provider">Unregistered keyed provider.</param>
    [Test]
    public void KeyedProvider([FromKeyedServices("missing")] IServiceProvider provider) => Assert.Fail("unreachable-body");
}
