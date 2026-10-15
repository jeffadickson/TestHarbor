using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TestHarbor;

/// <summary>Describes a copied evidence artifact with stable session identity and content integrity.</summary>
/// <param name="Path">Absolute retained artifact path.</param>
/// <param name="Owner">Session or test identifier.</param>
/// <param name="Created">UTC publication time.</param>
/// <param name="MediaType">Declared media type.</param>
/// <param name="Length">Published byte count.</param>
/// <param name="Sha256">Hexadecimal SHA-256 of published content.</param>
public sealed record ArtifactEvidence(string Path, string Owner, DateTimeOffset Created, string MediaType, long Length, string Sha256);

/// <summary>Copies bounded evidence into a retained directory and writes a provenance sidecar.</summary>
/// <remarks>Artifacts survive session cleanup for runner attachment and caller archival. Binary redaction
/// is the caller's responsibility. Text redaction replaces only explicitly supplied literal secrets.</remarks>
public sealed class ArtifactStore
{
    /// <summary>Stores the absolute retained output directory.</summary>
    private readonly string root;
    /// <summary>Bounds every published artifact.</summary>
    private readonly long maximumBytes;

    /// <summary>Creates an evidence publisher with an explicit storage root and size limit.</summary>
    /// <param name="root">Caller-controlled retained directory.</param>
    /// <param name="maximumBytes">Maximum artifact size, defaulting to sixteen MiB.</param>
    public ArtifactStore(string root, long maximumBytes = 16 * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        this.root = System.IO.Path.GetFullPath(root);
        this.maximumBytes = maximumBytes;
        Directory.CreateDirectory(this.root);
    }

    /// <summary>Publishes bounded binary evidence without altering its content.</summary>
    /// <param name="source">Source stream, left open and read from its current position.</param>
    /// <param name="name">Plain filename without any directory component.</param>
    /// <param name="owner">Non-secret session or test identity.</param>
    /// <param name="mediaType">Declared media type.</param>
    /// <param name="cancellationToken">Publication cancellation.</param>
    /// <returns>Provenance for the retained copy.</returns>
    public async Task<ArtifactEvidence> PublishAsync(Stream source, string name, string owner,
        string mediaType = "application/octet-stream", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        if (owner.Length > 1024 || mediaType.Length > 256 || name.Length > 200)
            throw new ArgumentException("Artifact metadata exceeds supported length limits.");
        if (name is "." or ".." || name.IndexOfAny(['/', '\\', ':']) >= 0 || name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Artifact name must be a plain filename.", nameof(name));
        var path = System.IO.Path.Combine(root, Guid.NewGuid().ToString("N") + "-" + name);
        try
        {
            long length = 0;
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
                {
                    length += count;
                    if (length > maximumBytes) throw new IOException("Artifact exceeds configured size limit.");
                    digest.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                }
            }
            var evidence = new ArtifactEvidence(path, owner, DateTimeOffset.UtcNow, mediaType, length,
                Convert.ToHexString(digest.GetHashAndReset()));
            await File.WriteAllTextAsync(path + ".json", JsonSerializer.Serialize(evidence), cancellationToken).ConfigureAwait(false);
            return evidence;
        }
        catch { File.Delete(path); File.Delete(path + ".json"); throw; }
    }

    /// <summary>Publishes UTF-8 text after replacing explicit literal secret values.</summary>
    /// <param name="text">Text evidence.</param>
    /// <param name="name">Plain artifact filename.</param>
    /// <param name="owner">Non-secret session identity.</param>
    /// <param name="secrets">Literal nonempty values to redact; longer matches are replaced first.</param>
    /// <param name="cancellationToken">Publication cancellation.</param>
    /// <returns>Provenance for the redacted copy.</returns>
    public async Task<ArtifactEvidence> PublishTextAsync(string text, string name, string owner,
        IEnumerable<string>? secrets = null, CancellationToken cancellationToken = default)
    {
        foreach (var secret in (secrets ?? []).Where(x => !string.IsNullOrEmpty(x)).OrderByDescending(x => x.Length))
            text = text.Replace(secret, "[redacted]", StringComparison.Ordinal);
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return await PublishAsync(input, name, owner, "text/plain; charset=utf-8", cancellationToken).ConfigureAwait(false);
    }
}
