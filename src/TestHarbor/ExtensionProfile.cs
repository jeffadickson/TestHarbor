using Microsoft.Extensions.DependencyInjection;

namespace TestHarbor;

/// <summary>Composes explicit service and extension registrations in deterministic insertion order.</summary>
/// <remarks>Profiles never activate by assembly scanning. Compose profiles explicitly; duplicate names
/// are rejected. Services follow normal Microsoft DI ownership and lifetime rules.</remarks>
public sealed class ExtensionProfile
{
    /// <summary>Stores named registrations in the caller-selected order.</summary>
    private readonly List<(string Name, Action<IServiceCollection> Register)> entries = [];

    /// <summary>Adds a named registration operation.</summary>
    /// <param name="name">Unique ordinal name within this profile.</param>
    /// <param name="register">Operation registering services without acquiring hardware.</param>
    /// <returns>This profile for fluent composition.</returns>
    public ExtensionProfile Add(string name, Action<IServiceCollection> register)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(register);
        if (entries.Any(x => x.Name == name)) throw new ArgumentException("Duplicate profile entry: " + name, nameof(name));
        entries.Add((name, register));
        return this;
    }

    /// <summary>Copies another profile's registrations, preserving order and rejecting duplicate names atomically.</summary>
    /// <param name="profile">Profile to append.</param>
    /// <returns>This profile with copied entries.</returns>
    public ExtensionProfile Include(ExtensionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var copy = profile.entries.ToArray();
        if (copy.Any(x => entries.Any(e => e.Name == x.Name))) throw new ArgumentException("Profiles contain duplicate entry names.", nameof(profile));
        entries.AddRange(copy);
        return this;
    }

    /// <summary>Applies a snapshot of this profile's registrations to a service collection.</summary>
    /// <param name="services">Target service registrations.</param>
    public void Apply(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var entry in entries.ToArray()) entry.Register(services);
    }
}
