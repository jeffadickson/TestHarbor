using Microsoft.Extensions.DependencyInjection;

namespace TestHarbor.Example.Injection;

/// <summary>Represents an application service whose implementation selects a keyed endpoint.</summary>
/// <param name="endpoint">Endpoint selected by Microsoft DI during transitive activation.</param>
public sealed class KeyedOrder([FromKeyedServices("primary")] KeyedEndpoint endpoint)
{
    /// <summary>Gets the order service's selected endpoint.</summary>
    /// <value>The primary endpoint belonging to the attempt scope.</value>
    public KeyedEndpoint Endpoint { get; } = endpoint;
}
