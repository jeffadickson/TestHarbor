using Microsoft.Extensions.DependencyInjection;

namespace TestHarbor;

/// <summary>Owns one test's dependency scope and resource lease.</summary>
/// <remarks>Dispose only after test work has joined. Cleanup is awaited before resources are released.</remarks>
public sealed class TestSession : IAsyncDisposable
{
    private readonly ServiceProvider root;
    private readonly AsyncServiceScope scope;
    private readonly ResourceLease lease;
    private readonly TestSessionOptions options;
    private readonly TestExtensionContext context;
    private readonly List<ITestExtension> entered = [];
    private readonly object disposeGate = new();
    private Task? disposal;
    private int disposed;

    private TestSession(ServiceProvider root, AsyncServiceScope scope, ResourceLease lease, TestSessionOptions options)
    {
        this.root = root;
        this.scope = scope;
        this.lease = lease;
        this.options = options;
        context = new(scope.ServiceProvider, options.Owner!);
    }

    /// <summary>Gets the services scoped to this test session.</summary>
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

    private async Task CompleteDisposalAsync(TaskCompletionSource completion)
    {
        try { await CleanupAsync().ConfigureAwait(false); completion.SetResult(); }
        catch (Exception error) { completion.SetException(error); }
    }

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
