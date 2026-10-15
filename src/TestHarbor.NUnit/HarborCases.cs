using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using Microsoft.VisualBasic.FileIO;

namespace TestHarbor.NUnit;

/// <summary>Builds deterministic NUnit data cases without acquiring execution resources during discovery.</summary>
public static class HarborCases
{
    /// <summary>Creates typed cases with caller-selected conversion and stable names derived from source identity.</summary>
    /// <typeparam name="T">Converted input type.</typeparam>
    /// <param name="values">Deterministically ordered source strings.</param>
    /// <param name="parse">Explicit culture-aware conversion function.</param>
    /// <param name="sourceId">Stable non-secret source/version identity.</param>
    /// <returns>NUnit cases retaining ordinary filtering and runner behavior.</returns>
    public static IEnumerable<TestCaseData> Values<T>(IEnumerable<string> values, Func<string, CultureInfo, T> parse, string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        var index = 0;
        foreach (var value in values)
        {
            var identity = sourceId + ":" + index.ToString(CultureInfo.InvariantCulture) + ":" + value;
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
            T converted;
            try { converted = parse(value, CultureInfo.InvariantCulture); }
            catch (Exception error) when (error is FormatException or OverflowException or ArgumentException)
            { throw new FormatException($"Invalid case at index {index} in {sourceId}."); }
            yield return new TestCaseData(new object?[] { converted }).SetName("{m}[" + hash + "]")
                .SetProperty("TestHarbor.Source", sourceId).SetProperty("TestHarbor.CaseId", hash);
            index++;
        }
    }

    /// <summary>Reads quoted CSV rows and converts them with an explicit invariant-culture policy.</summary>
    /// <typeparam name="T">Converted case type.</typeparam>
    /// <param name="reader">CSV reader consumed and closed when enumeration completes.</param>
    /// <param name="parse">Row converter receiving fields in source order.</param>
    /// <param name="sourceId">Stable non-secret source/version identifier.</param>
    /// <param name="hasHeader">Whether the first row is a header and excluded from cases.</param>
    /// <returns>Ordinary NUnit cases with stable row identities.</returns>
    public static IEnumerable<TestCaseData> Csv<T>(TextReader reader,
        Func<IReadOnlyList<string>, CultureInfo, T> parse, string sourceId, bool hasHeader = true)
    {
        using var parser = new TextFieldParser(reader) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        if (hasHeader && !parser.EndOfData) parser.ReadFields();
        var rows = new List<string>();
        while (!parser.EndOfData) rows.Add(System.Text.Json.JsonSerializer.Serialize(parser.ReadFields()!));
        foreach (var test in Values(rows, (row, culture) => parse(
            System.Text.Json.JsonSerializer.Deserialize<string[]>(row)!, culture), sourceId)) yield return test;
    }

    /// <summary>Enumerates every named enum value as an ordinary typed NUnit case.</summary>
    /// <typeparam name="T">Enum input type.</typeparam>
    /// <returns>Cases ordered by the runtime's enum value ordering.</returns>
    public static IEnumerable<TestCaseData> EnumValues<T>() where T : struct, Enum =>
        System.Enum.GetValues<T>().Select(value => new TestCaseData(new object[] { value }));

    /// <summary>Provides explicit null and empty string cases.</summary>
    /// <returns>Two ordinary NUnit string cases.</returns>
    public static IEnumerable<TestCaseData> NullAndEmpty() =>
        [new TestCaseData(new object?[] { null }), new TestCaseData(new object[] { "" })];
}
