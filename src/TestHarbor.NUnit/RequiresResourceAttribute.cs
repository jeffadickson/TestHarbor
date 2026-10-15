using NUnit.Framework;

namespace TestHarbor.NUnit;

/// <summary>Declares a named resource required by a HarborFixture test or fixture.</summary>
/// <remarks>NUnit attaches this metadata to the actual discovered case, including overloaded and generic
/// methods. HarborFixture merges inherited fixture and method declarations with Resources overrides.
/// Exclusive access wins duplicates. Acquisition occurs during base setup, not NUnit scheduling.
/// This attribute alone does not acquire resources on fixtures that do not inherit HarborFixture.</remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequiresResourceAttribute : PropertyAttribute
{
    /// <summary>Identifies shared-read declarations in NUnit's public property hierarchy.</summary>
    internal const string SharedKey = "TestHarbor.Resource.SharedRead";
    /// <summary>Identifies exclusive declarations in NUnit's public property hierarchy.</summary>
    internal const string ExclusiveKey = "TestHarbor.Resource.Exclusive";

    /// <summary>Registers validated resource metadata for NUnit discovery.</summary>
    /// <param name="name">Nonempty, case-sensitive logical resource name.</param>
    /// <param name="access">Required mode; exclusive unless explicitly shared.</param>
    /// <exception cref="ArgumentException">The name is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The access mode is undefined.</exception>
    public RequiresResourceAttribute(string name, ResourceAccess access = ResourceAccess.Exclusive)
        : base(Key(access), ValidateName(name)) { }

    /// <summary>Selects the metadata key while rejecting undefined access values.</summary>
    /// <param name="access">Access mode to encode.</param>
    /// <returns>The corresponding reserved property name.</returns>
    private static string Key(ResourceAccess access) => access switch
    {
        ResourceAccess.SharedRead => SharedKey,
        ResourceAccess.Exclusive => ExclusiveKey,
        _ => throw new ArgumentOutOfRangeException(nameof(access), access, "Unknown resource access mode.")
    };

    /// <summary>Validates a resource name before it enters NUnit metadata.</summary>
    /// <param name="name">Name supplied by the declaration.</param>
    /// <returns>The unchanged case-sensitive name.</returns>
    private static string ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name;
    }

    /// <summary>Reads declarations attached by NUnit to the current case and its parent suites.</summary>
    /// <returns>Resource requests to merge with explicit fixture requirements.</returns>
    /// <remarks>No method-name lookup or NUnit internal API is used. Duplicate hierarchy entries
    /// are normalized by ResourceCoordinator before acquisition.</remarks>
    internal static IEnumerable<ResourceRequest> CurrentRequirements()
    {
        var test = TestContext.CurrentContext.Test;
        return test.AllPropertyValues(SharedKey).Select(x => new ResourceRequest((string)x, ResourceAccess.SharedRead))
            .Concat(test.AllPropertyValues(ExclusiveKey).Select(x => new ResourceRequest((string)x)));
    }
}
