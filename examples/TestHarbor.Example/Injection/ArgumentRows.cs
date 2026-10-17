using NUnit.Framework;

namespace TestHarbor.Example.Injection;

/// <summary>Supplies ordinary order data without accessing injection services.</summary>
public static class ArgumentRows
{
    /// <summary>Gets an external integer row.</summary>
    /// <value>One quantity.</value>
    public static IEnumerable<TestCaseData> External => [new TestCaseData(6)];

    /// <summary>Computes source data from an explicit source argument.</summary>
    /// <param name="factor">Quantity multiplier passed by NUnit.</param>
    /// <returns>One multiplied quantity.</returns>
    public static IEnumerable<TestCaseData> Multiplied(int factor) => [new TestCaseData(factor * 4)];

    /// <summary>Gets a row containing a single array argument.</summary>
    /// <value>One array-valued data row.</value>
    public static IEnumerable<TestCaseData> Arrays => [new TestCaseData((object)new[] { 2, 4 })];

    /// <summary>Gets a row with an expected decimal result.</summary>
    /// <value>A quantity and its expected order total.</value>
    public static IEnumerable<TestCaseData> Results => [new TestCaseData(4).Returns(5m)];
}
