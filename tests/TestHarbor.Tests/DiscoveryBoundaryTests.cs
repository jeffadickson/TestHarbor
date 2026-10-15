using System.Globalization;
using NUnit.Framework;
using TestHarbor.NUnit;

namespace TestHarbor.Tests;

/// <summary>Demonstrates discovery-time data versus runtime work within one reported NUnit test.</summary>
public sealed class DiscoveryBoundaryTests : HarborFixture
{
    /// <summary>Provides stable, pure discovery-time cases without acquiring equipment.</summary>
    /// <returns>Two ordinary discovered NUnit cases.</returns>
    public static IEnumerable<TestCaseData> Inputs() => HarborCases.Values(["1", "2"],
        (text, culture) => int.Parse(text, culture), "discovery-example-v1");
    /// <summary>Runs one discovered case in its own session scope.</summary>
    /// <param name="value">Discovered input.</param>
    [TestCaseSource(nameof(Inputs))]
    public void Discovered(int value) => Assert.That(value, Is.Positive);
    /// <summary>Shows that runtime-generated work remains one reportable test rather than new adapter nodes.</summary>
    [Test]
    public void RuntimeItems()
    {
        foreach (var item in Enumerable.Range(1, 2)) Assert.That(item, Is.Positive);
        TestContext.Out.WriteLine("runtime-items=2; reported-tests=1");
    }
}
