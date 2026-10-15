using System.Text.Json;

namespace TestHarbor;

/// <summary>Persists lifecycle evidence independently of the coordinator's bounded event history.</summary>
/// <remarks>Keep the journal alive through session cleanup. Attach its retained file after session completion.
/// Exception messages are redacted only for explicitly supplied literal secret values.</remarks>
public sealed class LifecycleJournal : IDisposable
{
    /// <summary>Serializes writes and disposal.</summary>
    private readonly object gate = new();
    /// <summary>Writes durable line-delimited observations.</summary>
    private readonly StreamWriter writer;
    /// <summary>Bounds the retained evidence file size.</summary>
    private readonly long maximumBytes;
    /// <summary>Counts UTF-8 bytes already written.</summary>
    private long bytes;
    /// <summary>Stores literal secret values to replace in diagnostic text.</summary>
    private readonly string[] secrets;
    /// <summary>Gets the absolute retained evidence path.</summary>
    /// <value>The caller-selected journal file.</value>
    public string Path { get; }

    /// <summary>Creates a new journal without overwriting existing evidence.</summary>
    /// <param name="path">New evidence file path in a caller-controlled directory.</param>
    /// <param name="secrets">Literal values to redact from owner and failure messages.</param>
    /// <param name="maximumBytes">Maximum journal size in UTF-8 bytes.</param>
    public LifecycleJournal(string path, IEnumerable<string>? secrets = null, long maximumBytes = 16 * 1024 * 1024)
    {
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        this.maximumBytes = maximumBytes;
        Path = System.IO.Path.GetFullPath(path);
        this.secrets = (secrets ?? []).Where(x => !string.IsNullOrEmpty(x)).OrderByDescending(x => x.Length).ToArray();
        writer = new StreamWriter(new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
    }

    /// <summary>Appends one session observation as a flushed JSON record.</summary>
    /// <param name="observation">Observed boundary and failures known at that point.</param>
    public void Record(LifecycleObservation observation)
    {
        lock (gate)
        {
            var line = JsonSerializer.Serialize(new
            {
                Phase = observation.Phase.ToString(), Owner = Redact(observation.Owner), observation.Timestamp,
                observation.IsFinal, Errors = observation.Errors.Select(x => new { Type = x.GetType().FullName, Message = Redact(x.Message) })
            });
            var length = System.Text.Encoding.UTF8.GetByteCount(line + Environment.NewLine);
            if (bytes + length > maximumBytes) throw new IOException("Lifecycle journal exceeds its configured size limit.");
            writer.WriteLine(line);
            bytes += length;
        }
    }

    /// <summary>Redacts configured literal secret values from a diagnostic string.</summary>
    /// <param name="value">Diagnostic text.</param>
    /// <returns>Text with all configured values replaced.</returns>
    private string Redact(string value)
    {
        foreach (var secret in secrets) value = value.Replace(secret, "[redacted]", StringComparison.Ordinal);
        return value;
    }

    /// <summary>Closes the journal while preserving the retained evidence file.</summary>
    public void Dispose() { lock (gate) writer.Dispose(); }
}
