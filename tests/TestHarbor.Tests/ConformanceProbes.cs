using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor.NUnit;

namespace TestHarbor.Tests;

/// <summary>Supplies real-runner outcomes and independent lifecycle evidence for extension conformance.</summary>
[Explicit("Run using tools/verify_conformance.py.")]
[Parallelizable(ParallelScope.All)]
public sealed class ConformanceProbes : HarborFixture
{
    /// <summary>Gets the selected conformance scenario.</summary>
    /// <value>The test case's scenario argument.</value>
    private string Mode => (string)TestContext.CurrentContext.Test.Arguments[0]!;
    /// <summary>Gets the scenario-specific resource retained across all callbacks.</summary>
    /// <value>The exclusive scenario resource.</value>
    protected override IEnumerable<ResourceRequest> Resources => [new("Conformance/" + Mode)];
    /// <summary>Exercises conditions before resource acquisition or service initialization.</summary>
    /// <value>One failing condition for each condition scenario, otherwise none.</value>
    protected override IEnumerable<ExecutionCondition> Conditions => Mode.StartsWith("condition", StringComparison.Ordinal)
        ? [new("conformance-" + Mode, _ => false, Mode switch
        {
            "conditionSkip" => ConditionDisposition.Skip,
            "conditionAssume" => ConditionDisposition.Inconclusive,
            _ => ConditionDisposition.Fail
        })] : [];
    /// <summary>Registers the extension under test.</summary>
    /// <param name="services">Session registrations.</param>
    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddScoped<ITestExtension>(_ => new Extension(Mode));
    /// <summary>Records derived setup after extension initialization.</summary>
    [SetUp]
    public void Setup() => Record(Mode, "setup");
    /// <summary>Exercises successful, skipped, assumed and failing extension lifecycles.</summary>
    /// <param name="mode">Scenario name.</param>
    [TestCase("pass")]
    [TestCase("skip")]
    [TestCase("assume")]
    [TestCase("start")]
    [TestCase("stop")]
    [TestCase("cancel")]
    [TestCase("conditionSkip")]
    [TestCase("conditionAssume")]
    [TestCase("conditionFail")]
    public void Execute(string mode)
    {
        Record(mode, "body");
        if (mode == "skip") Assert.Ignore("conformance-skip");
        if (mode == "assume") Assume.That(false, Is.True, "conformance-assume");
    }
    /// <summary>Records derived teardown while ownership remains active.</summary>
    [TearDown]
    public void Teardown() => Record(Mode, "teardown");
    /// <summary>Verifies ownership ended and cleanup failure quarantined only its scenario resource.</summary>
    [OneTimeTearDown]
    public static void VerifyRelease()
    {
        foreach (var mode in new[] { "pass", "skip", "assume", "start", "stop", "cancel" })
        {
            var state = ResourceCoordinator.Shared.GetSnapshot().SingleOrDefault(x => x.Name == "Conformance/" + mode);
            Assert.That(state is null || (!state.Exclusive && state.Readers == 0), Is.True);
            if (mode == "stop")
            {
                Assert.That(state?.QuarantineReason, Is.Not.Null);
                ResourceCoordinator.Shared.ResetQuarantine("Conformance/stop", "Simulation recovery verified.");
            }
        }
    }
    /// <summary>Writes a lifecycle event and independently checks resource retention.</summary>
    /// <param name="mode">Scenario whose evidence file is appended.</param>
    /// <param name="phase">Observed phase.</param>
    private static void Record(string mode, string phase)
    {
        var state = ResourceCoordinator.Shared.GetSnapshot().Single(x => x.Name == "Conformance/" + mode);
        Assert.That(state.Exclusive, Is.True, "Lease must span " + phase);
        var directory = Environment.GetEnvironmentVariable("TESTHARBOR_PROBE_OUTPUT")
            ?? throw new InvalidOperationException("Use the conformance verifier.");
        File.AppendAllLines(Path.Combine(directory, mode + ".txt"), [phase]);
    }
    /// <summary>Records extension callbacks and injects selected startup or stop faults.</summary>
    /// <param name="mode">Scenario name.</param>
    private sealed class Extension(string mode) : ITestExtension, IAsyncDisposable
    {
        /// <summary>Records startup and injects initialization failure or cooperative cancellation.</summary>
        /// <param name="context">Session context.</param>
        /// <param name="cancellationToken">Startup token.</param>
        /// <returns>A completed startup task when no fault is selected.</returns>
        public Task StartAsync(TestExtensionContext context, CancellationToken cancellationToken)
        {
            Record(mode, "start");
            if (mode == "start") throw new IOException("conformance-start");
            if (mode == "cancel") throw new OperationCanceledException("conformance-cancel", new CancellationToken(true));
            return Task.CompletedTask;
        }
        /// <summary>Records cleanup and injects a selected stop failure.</summary>
        /// <param name="context">Session context.</param>
        /// <returns>A completed stop task when no fault is selected.</returns>
        public Task StopAsync(TestExtensionContext context)
        {
            Record(mode, "stop");
            if (mode == "stop") throw new IOException("conformance-stop");
            return Task.CompletedTask;
        }
        /// <summary>Records service disposal after extension stop.</summary>
        /// <returns>A completed disposal operation.</returns>
        public ValueTask DisposeAsync() { Record(mode, "dispose"); return ValueTask.CompletedTask; }
    }
}
