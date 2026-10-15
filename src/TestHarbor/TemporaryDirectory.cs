namespace TestHarbor;

/// <summary>Specifies when a session-created directory is retained.</summary>
public enum DirectoryRetention
{
    /// <summary>Deletes the directory regardless of observed outcome.</summary>
    Never,
    /// <summary>Retains the directory when the session observes a failure.</summary>
    OnFailure,
    /// <summary>Retains the directory for caller-managed archival.</summary>
    Always
}

/// <summary>Identifies a unique session-created directory with outcome-aware cleanup.</summary>
/// <remarks>Retention uses session-observed failures, including cleanup errors known before deletion.
/// A later NUnit runner error cannot restore a deleted directory. Use Always when final runner results matter.</remarks>
public sealed class TemporaryDirectory
{
    /// <summary>Stores the retention policy selected by the caller.</summary>
    private readonly DirectoryRetention retention;
    /// <summary>Gets the absolute path created for this session.</summary>
    /// <value>A unique directory under the caller's selected root.</value>
    public string Path { get; }

    /// <summary>Creates a unique directory under a validated ordinary directory.</summary>
    /// <param name="root">Parent directory, created if necessary.</param>
    /// <param name="retention">Policy controlling deletion at session cleanup.</param>
    internal TemporaryDirectory(string root, DirectoryRetention retention)
    {
        if (!Enum.IsDefined(retention)) throw new ArgumentOutOfRangeException(nameof(retention));
        var parent = System.IO.Path.GetFullPath(root);
        Directory.CreateDirectory(parent);
        parent = CanonicalDirectory(new DirectoryInfo(parent));
        ValidatePath(parent);
        Path = System.IO.Path.Combine(parent, "testharbor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
        this.retention = retention;
    }

    /// <summary>Deletes the owned directory only when the configured policy permits it.</summary>
    /// <param name="failed">Whether the session has observed any failure.</param>
    internal void Complete(bool failed)
    {
        if (retention == DirectoryRetention.Always || (failed && retention == DirectoryRetention.OnFailure)) return;
        if (!Directory.Exists(Path)) return;
        ValidatePath(Path);
        foreach (var entry in Directory.EnumerateFileSystemEntries(Path)) ValidateTree(entry);
        Directory.Delete(Path, true);
    }

    /// <summary>Resolves an explicitly selected parent to an ordinary absolute directory.</summary>
    /// <param name="directory">Existing parent directory.</param>
    /// <returns>A canonical path with ancestor links resolved.</returns>
    private static string CanonicalDirectory(DirectoryInfo directory)
    {
        if (directory.LinkTarget is not null)
            return CanonicalDirectory((DirectoryInfo)(directory.ResolveLinkTarget(true)
                ?? throw new IOException("Cannot resolve temporary parent.")));
        return directory.Parent is null ? directory.FullName
            : System.IO.Path.Combine(CanonicalDirectory(directory.Parent), directory.Name);
    }

    /// <summary>Rejects links inside the owned tree before recursive deletion.</summary>
    /// <param name="path">Entry whose children are checked without following links.</param>
    private static void ValidateTree(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Refusing cleanup of a directory containing a link: " + path);
        if (Directory.Exists(path))
            foreach (var entry in Directory.EnumerateFileSystemEntries(path)) ValidateTree(entry);
    }

    /// <summary>Rejects symbolic links and junctions in a directory's ancestor chain.</summary>
    /// <param name="path">Absolute directory path to inspect.</param>
    private static void ValidatePath(string path)
    {
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A temporary directory root cannot contain links: " + current.FullName);
    }
}
