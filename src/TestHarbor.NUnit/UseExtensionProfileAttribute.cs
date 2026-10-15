using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace TestHarbor.NUnit;

/// <summary>Defines an explicitly activated reusable set of service and extension registrations.</summary>
public interface IExtensionProfile
{
    /// <summary>Registers scoped dependencies without acquiring physical equipment.</summary>
    /// <param name="services">Session-owned service registrations.</param>
    void Configure(IServiceCollection services);
}

/// <summary>Activates an explicit profile on a fixture or method using NUnit's public metadata hierarchy.</summary>
/// <remarks>Profiles run by ascending order, then ordinal assembly-qualified type name. Identical declarations
/// are deduplicated; conflicting orders for the same type fail. No assembly scanning occurs.</remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class UseExtensionProfileAttribute : PropertyAttribute
{
    /// <summary>Identifies profile declarations in the public NUnit property hierarchy.</summary>
    private const string Key = "TestHarbor.ExtensionProfile";
    /// <summary>Declares a publicly constructible profile type.</summary>
    /// <param name="profile">Concrete IExtensionProfile implementation with a public parameterless constructor.</param>
    /// <param name="order">Registration order relative to other profiles.</param>
    public UseExtensionProfileAttribute(Type profile, int order = 0) : base(Key, Encode(profile, order)) { }

    /// <summary>Validates the type and serializes deterministic activation metadata.</summary>
    /// <param name="type">Explicitly selected profile type.</param>
    /// <param name="order">Requested ordering value.</param>
    /// <returns>Serialized profile identity and ordering.</returns>
    private static string Encode(Type type, int order)
    {
        if (!typeof(IExtensionProfile).IsAssignableFrom(type) || type.IsAbstract || type.ContainsGenericParameters || type.GetConstructor(Type.EmptyTypes) is null)
            throw new ArgumentException("Profile must implement IExtensionProfile and have a public parameterless constructor.", nameof(type));
        return JsonSerializer.Serialize(new Declaration(type.AssemblyQualifiedName!, order));
    }

    /// <summary>Reads and validates the current test's explicitly declared profile ordering.</summary>
    /// <returns>Ordered registration callbacks for this test.</returns>
    internal static IReadOnlyList<IExtensionProfile> CurrentProfiles()
    {
        var declarations = TestContext.CurrentContext.Test.AllPropertyValues(Key).Cast<string>().Distinct(StringComparer.Ordinal)
            .Select(x => JsonSerializer.Deserialize<Declaration>(x)!).ToArray();
        if (declarations.GroupBy(x => x.Type).Any(g => g.Count() > 1)) throw new InvalidOperationException("Conflicting orders for the same profile type.");
        return declarations.OrderBy(x => x.Order).ThenBy(x => x.Type, StringComparer.Ordinal)
            .Select(x => (IExtensionProfile)Activator.CreateInstance(Type.GetType(x.Type, true)!)!).ToArray();
    }

    /// <summary>Stores explicit profile activation metadata.</summary>
    /// <param name="Type">Assembly-qualified profile type name.</param>
    /// <param name="Order">Registration ordering value.</param>
    private sealed record Declaration(string Type, int Order);
}
