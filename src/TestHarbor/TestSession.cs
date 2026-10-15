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

    /// <summary>Releases a shared-provider child rental instead of disposing its root.</summary>
    private readonly Action<bool>? releaseScope;
    /// <summary>Tracks session-created directories for cleanup after service disposal.</summary>
    private readonly List<TemporaryDirectory> directories = [];
    /// <summary>Collects failures reported through the session and observer callbacks.</summary>
    private readonly List<Exception> observedErrors = [];
    /// <summary>Collects observer failures that must fail the session at cleanup.</summary>
    private readonly List<Exception> observerErrors = [];
    /// <summary>Detects recursive session disposal from extension or service cleanup.</summary>
    private readonly AsyncLocal<bool> cleaning = new();

    /// <summary>Gets the owner for resources created outside dependency injection.</summary>
    /// <value>The session-owned reverse cleanup stack.</value>
    public OwnedResources Owned { get; } = new();

    /// <summary>Creates a unique temporary directory retained according to observed session outcome.</summary>
    /// <param name="root">Optional parent directory; defaults to the operating system temporary path.</param>
    /// <param name="retention">Retention policy, defaulting to failed-session evidence retention.</param>
    /// <returns>The session-owned temporary directory.</returns>
    public TemporaryDirectory CreateTemporaryDirectory(string? root = null, DirectoryRetention retention = DirectoryRetention.OnFailure)
    {
        lock (disposeGate)
        {
            ObjectDisposedException.ThrowIf(disposal is not null, this);
            var directory = new TemporaryDirectory(root ?? Path.GetTempPath(), retention);
            directories.Add(directory);
            return directory;
        }
    }

    /// <summary>Records a failure from caller-managed setup, body or teardown for outcome-aware cleanup.</summary>
    /// <param name="error">Failure that must remain visible to session observers.</param>
    /// <remarks>This records evidence only; callers must still propagate their failure to their test runner.</remarks>
    public void RecordFailure(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        lock (observedErrors) observedErrors.Add(error);
    }

    /// <summary>Publishes a detached phase observation while retaining observer failures.</summary>
    /// <param name="phase">Boundary being observed.</param>
    private void Observe(SessionPhase phase)
    {
        lock (observedErrors)
        {
            if (context.Observation?.IsFinal == true && phase == SessionPhase.CleanupOverdue) return;
            var observation = new LifecycleObservation(phase, options.Owner!, DateTimeOffset.UtcNow,
                Array.AsReadOnly(observedErrors.ToArray()), phase == SessionPhase.Finished);
            context.Observation = observation;
            try { options.Observer?.Invoke(observation); }
            catch (Exception error) { observerErrors.Add(error); observedErrors.Add(error); }
        }
    }

    /// <summary>Binds an owned service provider, async scope and resource lease into a test session.</summary>
    /// <param name="root">Root provider owning session-local singleton services.</param>
    /// <param name="scope">Async DI scope owning per-test services.</param>
    /// <param name="lease">Granted resource set retained through all cleanup.</param>
    /// <param name="options">Diagnostic identity and cleanup policy.</param>
    /// <param name="releaseScope">Optional callback releasing a reusable provider rental.</param>
    private TestSession(ServiceProvider root, AsyncServiceScope scope, ResourceLease lease, TestSessionOptions options, Action<bool>? releaseScope = null)
    {
        this.root = root;
        this.scope = scope;
        this.lease = lease;
        this.options = options;
        this.releaseScope = releaseScope;
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
        if (options?.ServiceScope is not null && configure is not null)
            throw new ArgumentException("Register services on the shared owner; session registration callbacks cannot override its provider.", nameof(configure));
        options = (options ?? new()) with { Owner = options?.Owner ?? Guid.NewGuid().ToString("N") };
        options.Budgets?.Validate();
        resources = resources.ToArray();
        options.ServiceScope?.ValidateResources(resources);
        using var acquisition = SessionBudgets.Start(options.Budgets?.Acquisition, cancellationToken);
        var lease = await (coordinator ?? ResourceCoordinator.Shared)
            .AcquireAsync(resources, acquisition.Token, options.Owner).ConfigureAwait(false);
        using var startup = SessionBudgets.Start(options.Budgets?.Startup, cancellationToken);
        ServiceProvider? provider = null;
        TestSession? session = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (options.ServiceScope is { } shared)
            {
                var rental = shared.Rent();
                session = new TestSession(rental.Provider, rental.Scope, lease, options, rental.Release);
            }
            else
            {
                var services = new ServiceCollection();
                configure?.Invoke(services);
                provider = services.BuildServiceProvider(new ServiceProviderOptions
                    { ValidateScopes = true, ValidateOnBuild = true });
                session = new TestSession(provider, provider.CreateAsyncScope(), lease, options);
            }
            session.Observe(SessionPhase.Starting);
            foreach (var extension in session.Services.GetServices<ITestExtension>())
            {
                startup.Token.ThrowIfCancellationRequested();
                // Register for rollback before calling Start: initialization can fail partway through.
                session.entered.Add(extension);
                await extension.StartAsync(session.context, startup.Token).ConfigureAwait(false);
            }
            startup.Token.ThrowIfCancellationRequested();
            session.Observe(SessionPhase.Ready);
            return session;
        }
        catch (Exception setupError)
        {
            session?.RecordFailure(setupError);
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
        using var budget = SessionBudgets.Start(options?.Budgets?.Body, cancellationToken);
        session.Observe(SessionPhase.BodyStarting);
        try { await body(session, budget.Token).ConfigureAwait(false); budget.Token.ThrowIfCancellationRequested(); }
        catch (Exception error) { bodyError = error; session.RecordFailure(error); throw; }
        finally
        {
            session.Observe(SessionPhase.BodyFinished);
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
        if (cleaning.Value) throw new InvalidOperationException("Recursive session disposal is unsupported.");
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
        cleaning.Value = true;
        try
        {
            var cleanup = CleanupAsync();
            if (options.Budgets?.CleanupWarningAfter is { } threshold)
            {
                using var timer = new CancellationTokenSource();
                var delay = Task.Delay(threshold, timer.Token);
                if (await Task.WhenAny(cleanup, delay).ConfigureAwait(false) == delay)
                    Observe(SessionPhase.CleanupOverdue);
                timer.Cancel();
            }
            await cleanup.ConfigureAwait(false);
            completion.SetResult();
        }
        catch (Exception error) { completion.SetException(error); }
        finally { cleaning.Value = false; }
    }

    /// <summary>Unwinds extensions, disposes services, quarantines failed cleanup and finally releases resources.</summary>
    /// <returns>A task completing after all cleanup attempts and lease release.</returns>
    private async Task CleanupAsync()
    {
        List<Exception> errors = [];
        Observe(SessionPhase.Cleaning);
        try
        {
            for (var index = entered.Count - 1; index >= 0; index--)
            {
                try { await entered[index].StopAsync(context).ConfigureAwait(false); }
                catch (Exception error) { errors.Add(error); }
            }
            try { await Owned.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { errors.Add(error); }
            try { await scope.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { errors.Add(error); }
            try
            {
                if (releaseScope is null) await root.DisposeAsync().ConfigureAwait(false);

            }
            catch (Exception error) { errors.Add(error); }
        }
        finally
        {
            foreach (var directory in directories.AsEnumerable().Reverse())
            {
                try
                {
                    bool failed;
                    lock (observedErrors) failed = observedErrors.Count != 0;
                    directory.Complete(failed || errors.Count != 0 || observerErrors.Count != 0);
                }
                catch (Exception error) { errors.Add(error); }
            }
            if (releaseScope is not null)
            {
                try { releaseScope(errors.Count > 0); }
                catch (Exception error) { errors.Add(error); }
            }
            try
            {
                if (errors.Count > 0 && options.QuarantineOnCleanupFailure)
                    lease.Quarantine("Session cleanup failed: " + string.Join("; ", errors.Select(x => x.Message)));
            }
            finally { lease.Dispose(); }
        }
        foreach (var error in errors) RecordFailure(error);
        Observe(SessionPhase.Finished);
        errors.AddRange(observerErrors);
        if (errors.Count > 0) throw new AggregateException("Session cleanup failed.", errors);
    }
}
