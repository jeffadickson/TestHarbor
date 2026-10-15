namespace TestHarbor;

/// <summary>A per-test async extension registered as a scoped ITestExtension service.</summary>
/// <remarks>Start follows DI registration order. Stop unwinds in reverse, including a partially failed Start.</remarks>
public interface ITestExtension
{
    /// <summary>Initializes extension state after resources and services are available.</summary>
    /// <param name="context">Services and owner identity for this test session.</param>
    /// <param name="cancellationToken">Cooperative setup cancellation.</param>
    /// <returns>A task completing when startup finishes, or faulting if initialization fails.</returns>
    Task StartAsync(TestExtensionContext context, CancellationToken cancellationToken);

    /// <summary>Cleans up partial or complete initialization; must tolerate a failed Start.</summary>
    /// <param name="context">The same context supplied to Start; services have not been disposed yet.</param>
    /// <returns>A task completing when cleanup finishes, or faulting if cleanup fails.</returns>
    Task StopAsync(TestExtensionContext context);
}

/// <summary>The context shared by extensions during one session.</summary>
/// <param name="Services">Per-test service provider.</param>
/// <param name="Owner">Diagnostic identity for this session.</param>
public sealed record TestExtensionContext(IServiceProvider Services, string Owner)
{
    /// <summary>Gets the latest session observation, which is not a final NUnit result.</summary>
    /// <value>The last observed boundary, or null before startup.</value>
    public LifecycleObservation? Observation { get; internal set; }
}

/// <summary>Controls session diagnostics and cleanup failure policy.</summary>
public sealed record TestSessionOptions
{
    /// <summary>Gets the diagnostic owner label; null generates a unique ID.</summary>
    /// <value>The diagnostic owner label for this request or session.</value>
    public string? Owner { get; init; }

    /// <summary>Gets cooperative deadlines and cleanup diagnostic thresholds.</summary>
    /// <value>Null for caller-token-only cancellation and no cleanup threshold.</value>
    public SessionBudgets? Budgets { get; init; }

    /// <summary>Gets an explicitly owned fixture/run provider for this session.</summary>
    /// <value>Null for a session-local root; otherwise a caller-owned reusable provider.</value>
    public TestServiceScope? ServiceScope { get; init; }

    /// <summary>Receives explicit session phase observations; observer exceptions fail cleanup and cannot suppress test failures.</summary>
    /// <value>An optional synchronous observer, called serially by the session.</value>
    public Action<LifecycleObservation>? Observer { get; init; }

    /// <summary>Gets whether extension/service cleanup failures quarantine this session's resources; defaults true.</summary>
    /// <value>True by default; false permits reuse after reporting cleanup failure.</value>
    public bool QuarantineOnCleanupFailure { get; init; } = true;
}
