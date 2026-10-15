namespace TestHarbor;

/// <summary>Specifies whether a resource can be shared with other readers.</summary>
public enum ResourceAccess
{
    /// <summary>Allows concurrent readers but excludes writers.</summary>
    SharedRead,
    /// <summary>Excludes all other owners.</summary>
    Exclusive
}

/// <summary>A case-sensitive logical resource name and its access requirement.</summary>
/// <param name="Name">Stable name shared by all tests addressing the same physical resource.</param>
/// <param name="Access">Required ownership mode; defaults to exclusive.</param>
public sealed record ResourceRequest(string Name, ResourceAccess Access = ResourceAccess.Exclusive);
