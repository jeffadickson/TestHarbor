using System.Collections;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace TestHarbor;

/// <summary>Defines one known configuration key and its default and disclosure policy.</summary>
/// <param name="Name">Ordinal key name, also used after the environment prefix.</param>
/// <param name="DefaultValue">Default string value, or null when optional.</param>
/// <param name="Secret">Whether diagnostics must redact this value.</param>
public sealed record ConfigurationSetting(string Name, string? DefaultValue = null, bool Secret = false);

/// <summary>Resolves known keys with defaults, JSON file, environment and test overrides in ascending precedence.</summary>
public sealed class TestConfiguration
{
    /// <summary>Stores effective string values.</summary>
    private readonly Dictionary<string, string?> values;
    /// <summary>Identifies keys hidden from diagnostics.</summary>
    private readonly HashSet<string> secrets;

    /// <summary>Constructs a validated immutable configuration snapshot.</summary>
    /// <param name="settings">Unique accepted keys and their defaults.</param>
    /// <param name="file">Optional JSON object containing string or null values.</param>
    /// <param name="environmentPrefix">Optional prefix selecting environment keys; unknown selected keys fail.</param>
    /// <param name="overrides">Optional highest-precedence per-test values.</param>
    public TestConfiguration(IEnumerable<ConfigurationSetting> settings, string? file = null,
        string? environmentPrefix = null, IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var schema = settings.ToArray();
        foreach (var setting in schema) ArgumentException.ThrowIfNullOrWhiteSpace(setting.Name);
        values = schema.ToDictionary(x => x.Name, x => x.DefaultValue, StringComparer.Ordinal);
        secrets = schema.Where(x => x.Secret).Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        if (file is not null)
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(file))
                ?? throw new ArgumentException("Configuration must be a JSON object.", nameof(file));
            foreach (var item in data) Set(item.Key, item.Value);
        }
        if (environmentPrefix is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(environmentPrefix);
            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
                if (((string)entry.Key).StartsWith(environmentPrefix, StringComparison.Ordinal))
                    Set(((string)entry.Key)[environmentPrefix.Length..], (string?)entry.Value);
        }
        if (overrides is not null) foreach (var item in overrides) Set(item.Key, item.Value);
    }

    /// <summary>Replaces a known value while rejecting misspelled or unsupported keys.</summary>
    /// <param name="name">Configured key.</param>
    /// <param name="value">New value.</param>
    private void Set(string name, string? value)
    {
        if (!values.ContainsKey(name)) throw new ArgumentException("Unknown configuration key: " + name);
        values[name] = value;
    }

    /// <summary>Reads an effective value as a caller-selected type using an explicit conversion policy.</summary>
    /// <typeparam name="T">Requested configuration type.</typeparam>
    /// <param name="name">Known key to read.</param>
    /// <param name="parse">Converter whose culture and validation rules are explicit.</param>
    /// <returns>The converted value.</returns>
    /// <remarks>Conversion errors omit the original value to avoid exposing secrets.</remarks>
    public T Get<T>(string name, Func<string, T> parse)
    {
        ArgumentNullException.ThrowIfNull(parse);
        if (!values.TryGetValue(name, out var value) || value is null) throw new ArgumentException("Missing configuration key: " + name);
        try { return parse(value); }
        catch (Exception error) when (error is FormatException or OverflowException or ArgumentException)
        { throw new FormatException("Invalid configuration value for key: " + name); }
    }

    /// <summary>Gets a detached diagnostic snapshot with secret values replaced.</summary>
    /// <returns>Effective non-secret values and redacted secret placeholders.</returns>
    public IReadOnlyDictionary<string, string?> Describe() => new ReadOnlyDictionary<string, string?>(
        values.ToDictionary(x => x.Key, x => secrets.Contains(x.Key) ? "[redacted]" : x.Value, StringComparer.Ordinal));
}

/// <summary>Specifies the result when an execution condition is unmet.</summary>
public enum ConditionDisposition
{
    /// <summary>Reports an intentionally skipped test.</summary>
    Skip,
    /// <summary>Reports an unproven runtime assumption.</summary>
    Inconclusive,
    /// <summary>Reports a setup failure.</summary>
    Fail
}

/// <summary>Defines a pure condition evaluated before test resources or services are acquired.</summary>
/// <param name="Reason">Non-secret explanation for an unmet condition.</param>
/// <param name="Predicate">Pure predicate evaluated against configuration.</param>
/// <param name="Disposition">Runner result when the predicate returns false.</param>
public sealed record ExecutionCondition(string Reason, Func<TestConfiguration, bool> Predicate,
    ConditionDisposition Disposition = ConditionDisposition.Skip);
