using Microsoft.Extensions.DependencyInjection;

namespace TestHarbor;

/// <summary>Invokes typed service-dependent bodies without replacing NUnit construction or parameter binding.</summary>
public static class ServiceInvocation
{
    /// <summary>Resolves one service while keeping ordinary test data in the caller's closure.</summary>
    /// <typeparam name="T">Required service type.</typeparam>
    /// <param name="session">Active session owning the service scope.</param>
    /// <param name="body">Async typed body that must join all work before returning.</param>
    /// <param name="cancellationToken">Cooperative body cancellation.</param>
    /// <returns>The body completion.</returns>
    /// <remarks>Exactly one service registration is required; multiple registrations are rejected
    /// instead of silently choosing the last one.</remarks>
    public static Task InvokeAsync<T>(TestSession session, Func<T, CancellationToken, Task> body,
        CancellationToken cancellationToken = default) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(body);
        var matches = session.Services.GetServices<T>().ToArray();
        if (matches.Length != 1) throw new InvalidOperationException($"Expected one {typeof(T).FullName} service; found {matches.Length}.");
        return body(matches[0], cancellationToken);
    }
}
