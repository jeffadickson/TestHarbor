namespace TestHarbor;

/// <summary>A per-test async extension registered as a scoped ITestExtension service.</summary>
/// <remarks>Start follows DI registration order. Stop unwinds in reverse, including a partially failed Start.</remarks>
public interface ITestExtension
{
    /// <summary>Initializes extension state after resources and services are available.</summary>
    /// <param name="context">Services and owner identity for this test session.</param>
    /// <param name="cancellationToken">Cooperative setup cancellation.</param>
    Task StartAsync(TestExtensionContext context, CancellationToken cancellationToken);

    /// <summary>Cleans up partial or complete initialization; must tolerate a failed Start.</summary>
    /// <param name="context">The same context supplied to Start; services have not been disposed yet.</param>
    Task StopAsync(TestExtensionContext context);
}

/// <summary>The context shared by extensions during one session.</summary>
/// <param name="Services">Per-test service provider.</param>
/// <param name="Owner">Diagnostic identity for this session.</param>
public sealed record TestExtensionContext(IServiceProvider Services, string Owner);

/// <summary>Controls session diagnostics and cleanup failure policy.</summary>
public sealed record TestSessionOptions
{
    /// <summary>Gets the diagnostic owner label; null generates a unique ID.</summary>
    public string? Owner { get; init; }

    /// <summary>Gets whether extension/service cleanup failures quarantine this session's resources; defaults true.</summary>
    public bool QuarantineOnCleanupFailure { get; init; } = true;
}
