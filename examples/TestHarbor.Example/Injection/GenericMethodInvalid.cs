using NUnit.Framework;
using TestHarbor.NUnit.Injection;

namespace TestHarbor.Example.Injection;

/// <summary>Verifies generic discovery errors remain visible and never activate services.</summary>
[HarborInjectedFixture(typeof(IgnoredActivationProfile)), Explicit("Run generic injection verifier")]
public sealed class GenericMethodInvalid
{
    /// <summary>Rejects a reference-type case violating a value-type constraint.</summary>
    /// <typeparam name="T">Type required to be a value type.</typeparam>
    /// <param name="value">Invalid reference-type data.</param>
    /// <param name="service">Dependency that must never resolve.</param>
    [TestCase("book")]
    public void Constraint<T>(T value, [FromServices] GenericRepository<T> service) where T : struct => Assert.Fail("unreachable-body");

    /// <summary>Requires explicit type arguments when only services mention the generic type.</summary>
    /// <typeparam name="T">Type with no ordinary data inference source.</typeparam>
    /// <param name="service">Service excluded from NUnit inference.</param>
    [TestCase]
    public void Uninferable<T>([FromServices] GenericRepository<T> service) => Assert.Fail("unreachable-body");
}
