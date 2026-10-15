using Microsoft.Extensions.DependencyInjection;

namespace TestHarbor;

/// <summary>Owns a reusable provider for an explicitly bounded fixture or run.</summary>
/// <remarks>Create this owner only for selected work. Session scopes isolate scoped services while sharing
/// singletons. Dispose sessions before awaiting owner disposal. No global service locator is used.</remarks>
public sealed class TestServiceScope : IAsyncDisposable
{
    /// <summary>Retains optional scope-lifetime resources until singleton cleanup finishes.</summary>
    private ResourceLease? lease;
    /// <summary>Tracks resources already owned at this scope to reject recursive child requests.</summary>
    private HashSet<string> resourceNames = new(StringComparer.Ordinal);
    /// <summary>Rejects further child scopes after child cleanup has failed.</summary>
    private bool faulted;
    /// <summary>Detects recursive root disposal from singleton cleanup.</summary>
    private readonly AsyncLocal<bool> cleaning = new();
    /// <summary>Protects child rental counts and closure publication.</summary>
    private readonly object gate = new();
    /// <summary>Owns shared singleton services.</summary>
    private readonly ServiceProvider provider;
    /// <summary>Completes after every active child has released its scope.</summary>
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>Counts active session scopes.</summary>
    private int children;
    /// <summary>Prevents new sessions once closure begins.</summary>
    private bool closing;
    /// <summary>Publishes a single disposal result to concurrent callers.</summary>
    private Task? disposal;

    /// <summary>Builds a provider with scope and registration validation enabled.</summary>
    /// <param name="configure">Registrations for shared singletons and per-session scoped services.</param>
    public TestServiceScope(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var services = new ServiceCollection();
        configure(services);
        provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    /// <summary>Acquires scope-lifetime resources before building shared service registrations.</summary>
    /// <param name="resources">Resources distinct from child-session requests to avoid recursive acquisition.</param>
    /// <param name="configure">Shared provider registrations without direct equipment access.</param>
    /// <param name="coordinator">Optional coordinator used for the entire owner lifetime.</param>
    /// <param name="cancellationToken">Acquisition cancellation.</param>
    /// <returns>A reusable owner retaining resources through singleton disposal.</returns>
    public static async Task<TestServiceScope> OpenAsync(IEnumerable<ResourceRequest> resources,
        Action<IServiceCollection> configure, ResourceCoordinator? coordinator = null, CancellationToken cancellationToken = default)
    {
        var requests = resources.ToArray();
        var lease = await (coordinator ?? ResourceCoordinator.Shared).AcquireAsync(requests, cancellationToken).ConfigureAwait(false);
        try { cancellationToken.ThrowIfCancellationRequested(); return new TestServiceScope(configure) { lease = lease, resourceNames = requests.Select(x => x.Name).ToHashSet(StringComparer.Ordinal) }; }
        catch { lease.Dispose(); throw; }
    }

    /// <summary>Rejects session requirements that would reacquire resources already held by this owner.</summary>
    /// <param name="requests">Proposed session requirements.</param>
    internal void ValidateResources(IEnumerable<ResourceRequest> requests)
    {
        if (requests.Any(x => resourceNames.Contains(x.Name)))
            throw new InvalidOperationException("A session cannot reacquire a resource held by its shared service owner.");
    }

    /// <summary>Rents an isolated scope while preventing closure races.</summary>
    /// <returns>A provider, child scope and release callback owned by the caller.</returns>
    internal (ServiceProvider Provider, AsyncServiceScope Scope, Action<bool> Release) Rent()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            if (faulted) throw new InvalidOperationException("Shared scope had a cleanup failure; close it and verify recovery.");
            var scope = provider.CreateAsyncScope();
            children++;
            return (provider, scope, Release);
        }
    }

    /// <summary>Records that a child has finished its cleanup attempts.</summary>
    /// <param name="failed">Whether the child reported any cleanup failure.</param>
    private void Release(bool failed)
    {
        lock (gate)
        {
            if (failed) { faulted = true; lease?.Quarantine("Child cleanup failed in shared scope."); }
            children--;
            if (closing && children == 0) drained.TrySetResult();
        }
    }

    /// <summary>Stops accepting sessions, waits for existing children, then disposes shared services.</summary>
    /// <returns>The shared owner cleanup result, including singleton cleanup failures.</returns>
    /// <remarks>A singleton cleanup failure is reported to this owner. Hardware shared at this scope
    /// must have a separately held scope-lifetime lease; per-test leases do not protect singleton cleanup.</remarks>
    public ValueTask DisposeAsync()
    {
        if (cleaning.Value) throw new InvalidOperationException("Recursive shared-scope disposal is unsupported.");
        lock (gate)
        {
            closing = true;
            if (children == 0) drained.TrySetResult();
            if (disposal is null)
            {
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                disposal = completion.Task;
                _ = CompleteCloseAsync(completion);
            }
            return new(disposal);
        }
    }

    /// <summary>Completes the already-published shared root disposal task.</summary>
    /// <param name="completion">Shared completion promise.</param>
    /// <returns>A task completing after root cleanup.</returns>
    private async Task CompleteCloseAsync(TaskCompletionSource completion)
    {
        cleaning.Value = true;
        try { await CloseAsync().ConfigureAwait(false); completion.SetResult(); }
        catch (Exception error) { completion.SetException(error); }
        finally { cleaning.Value = false; }
    }

    /// <summary>Disposes the root after all child scopes have completed.</summary>
    /// <returns>A task completing after root cleanup.</returns>
    private async Task CloseAsync()
    {
        await drained.Task.ConfigureAwait(false);
        try { await provider.DisposeAsync().ConfigureAwait(false); }
        catch
        {
            lease?.Quarantine("Shared service cleanup failed; verify recovery before reuse.");
            throw;
        }
        finally { lease?.Dispose(); }
    }
}
