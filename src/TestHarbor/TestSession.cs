using Microsoft.Extensions.DependencyInjection;

namespace TestHarbor;

/// <summary>Owns one test's dependency scope and resource lease.</summary>
/// <remarks>Dispose only after test work has joined. Cleanup is awaited before resources are released.</remarks>
public sealed class TestSession : IAsyncDisposable
{
    /// <summary>Owns session-local singleton services and dependency registration metadata.</summary>
    private readonly ServiceProvider root;
    /// <summary>Owns scoped services created for this test session.</summary>
    private readonly AsyncServiceScope scope;
    /// <summary>Retains all session resources until extension and service cleanup has finished.</summary>
    private readonly ResourceLease lease;
    /// <summary>Stores the diagnostic identity and quarantine policy selected when the session opened.</summary>
    private readonly TestSessionOptions options;
    /// <summary>Provides extensions with the per-test services and diagnostic identity during start and stop.</summary>
    private readonly TestExtensionContext context;
    /// <summary>Records extensions before startup so partial initialization can be unwound in reverse.</summary>
    private readonly List<ITestExtension> entered = [];
    /// <summary>Protects publication of the shared cleanup task for concurrent disposal callers.</summary>
    private readonly object disposeGate = new();
    /// <summary>Stores the single cleanup task observed by all disposal callers.</summary>
    private Task? disposal;
    /// <summary>Prevents new service resolution through the session once disposal has started.</summary>
    private int disposed;

    /// <summary>Binds an owned service provider, async scope and resource lease into a test session.</summary>
    /// <param name="root">Root provider owning session-local singleton services.</param>
    /// <param name="scope">Async DI scope owning per-test services.</param>
    /// <param name="lease">Granted resource set retained through all cleanup.</param>
    /// <param name="options">Diagnostic identity and cleanup policy.</param>
    private TestSession(ServiceProvider root, AsyncServiceScope scope, ResourceLease lease, TestSessionOptions options)
    {
        this.root = root;
        this.scope = scope;
        this.lease = lease;
        this.options = options;
        context = new(scope.ServiceProvider, options.Owner!);
    }

    /// <summary>Gets the services scoped to this test session.</summary>
    /// <value>The per-test service provider; unavailable outside the active session.</value>
    public IServiceProvider Services => Volatile.Read(ref disposed) == 0
        ? scope.ServiceProvider : throw new ObjectDisposedException(nameof(TestSession));

    /// <summary>Acquires resources, then creates a validated dependency scope.</summary>
    /// <param name="resources">All resources needed by setup, test, and cleanup.</param>
    /// <param name="configure">Registers services without accessing physical resources.</param>
    /// <param name="coordinator">Optional coordinator; defaults to the process-wide instance.</param>
    /// <param name="cancellationToken">Cancels resource acquisition.</param>
    /// <param name="options">Cleanup failure policy and diagnostic identity.</param>
    /// <returns>A session to dispose asynchronously.</returns>
    public static async Task<TestSession> OpenAsync(IEnumerable<ResourceRequest> resources,
        Action<IServiceCollection>? configure = null, ResourceCoordinator? coordinator = null,
        CancellationToken cancellationToken = default, TestSessionOptions? options = null)
    {
        options = (options ?? new()) with { Owner = options?.Owner ?? Guid.NewGuid().ToString("N") };
        var lease = await (coordinator ?? ResourceCoordinator.Shared)
            .AcquireAsync(resources, cancellationToken, options.Owner).ConfigureAwait(false);
        ServiceProvider? provider = null;
        TestSession? session = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var services = new ServiceCollection();
            configure?.Invoke(services);
            provider = services.BuildServiceProvider(new ServiceProviderOptions
                { ValidateScopes = true, ValidateOnBuild = true });
            session = new TestSession(provider, provider.CreateAsyncScope(), lease, options);
            foreach (var extension in session.Services.GetServices<ITestExtension>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Register for rollback before calling Start: initialization can fail partway through.
                session.entered.Add(extension);
                await extension.StartAsync(session.context, cancellationToken).ConfigureAwait(false);
            }
            return session;
        }
        catch (Exception setupError)
        {
            try
            {
                if (session is not null) await session.DisposeAsync().ConfigureAwait(false);
                else
                {
                    try { if (provider is not null) await provider.DisposeAsync().ConfigureAwait(false); }
                    catch
                    {
                        if (options.QuarantineOnCleanupFailure) lease.Quarantine("Provider cleanup failed during setup.");
                        throw;
                    }
                    finally { lease.Dispose(); }
                }
            }
            catch (Exception cleanupError)
            { throw new AggregateException("Session setup and cleanup both failed.", setupError, cleanupError); }
            throw;
        }
    }

    /// <summary>Runs a body and awaits cleanup, preserving both errors if both phases fail.</summary>
    /// <param name="resources">Resources retained for the complete body and cleanup.</param>
    /// <param name="body">Async test work; await all child work before returning.</param>
    /// <param name="configure">Optional service registrations.</param>
    /// <param name="coordinator">Optional resource coordinator.</param>
    /// <param name="cancellationToken">Cooperative cancellation passed to the body.</param>
    /// <param name="options">Cleanup policy and diagnostic identity.</param>
    /// <returns>A task completing after the test body and cleanup; both failures are retained when necessary.</returns>
    /// <exception cref="AggregateException">The body and cleanup both failed, or cleanup has multiple reported failures.</exception>
    public static async Task RunAsync(IEnumerable<ResourceRequest> resources,
        Func<TestSession, CancellationToken, Task> body, Action<IServiceCollection>? configure = null,
        ResourceCoordinator? coordinator = null, CancellationToken cancellationToken = default,
        TestSessionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        var session = await OpenAsync(resources, configure, coordinator, cancellationToken, options).ConfigureAwait(false);
        Exception? bodyError = null;
        try { await body(session, cancellationToken).ConfigureAwait(false); }
        catch (Exception error) { bodyError = error; throw; }
        finally
        {
            try { await session.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanupError) when (bodyError is not null)
            { throw new AggregateException("Test body and cleanup both failed.", bodyError, cleanupError); }
        }
    }

    /// <summary>Marks the held resource set as requiring recovery, without releasing ownership.</summary>
    /// <param name="reason">Explanation recorded in resource snapshots.</param>
    public void QuarantineResources(string reason) => lease.Quarantine(reason);

    /// <summary>Stops extensions, disposes services, and then releases ownership.</summary>
    /// <remarks>Concurrent callers join the same cleanup task and observe the same errors.</remarks>
    /// <returns>A value task completing when cleanup finishes; cleanup failures propagate to the caller.</returns>
    /// <exception cref="AggregateException">One or more extension or service cleanup operations failed.</exception>
    public ValueTask DisposeAsync()
    {
        lock (disposeGate)
        {
            if (disposal is null)
            {
                Interlocked.Exchange(ref disposed, 1);
                // Publish the task before invoking extension code, which may synchronously reenter.
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                disposal = completion.Task;
                _ = CompleteDisposalAsync(completion);
            }
            return new ValueTask(disposal);
        }
    }

    /// <summary>Completes the published disposal promise with the cleanup result without losing exceptions.</summary>
    /// <param name="completion">Published promise that all disposal callers await.</param>
    /// <returns>A task completing after the shared disposal promise has been resolved.</returns>
    private async Task CompleteDisposalAsync(TaskCompletionSource completion)
    {
        try { await CleanupAsync().ConfigureAwait(false); completion.SetResult(); }
        catch (Exception error) { completion.SetException(error); }
    }

    /// <summary>Unwinds extensions, disposes services, quarantines failed cleanup and finally releases resources.</summary>
    /// <returns>A task completing after all cleanup attempts and lease release.</returns>
    private async Task CleanupAsync()
    {
        List<Exception> errors = [];
        try
        {
            for (var index = entered.Count - 1; index >= 0; index--)
            {
                try { await entered[index].StopAsync(context).ConfigureAwait(false); }
                catch (Exception error) { errors.Add(error); }
            }
            try { await scope.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { errors.Add(error); }
            try { await root.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { errors.Add(error); }
        }
        finally
        {
            try
            {
                if (errors.Count > 0 && options.QuarantineOnCleanupFailure)
                    lease.Quarantine("Session cleanup failed: " + string.Join("; ", errors.Select(x => x.Message)));
            }
            finally { lease.Dispose(); }
        }
        if (errors.Count > 0) throw new AggregateException("Session cleanup failed.", errors);
    }
}
