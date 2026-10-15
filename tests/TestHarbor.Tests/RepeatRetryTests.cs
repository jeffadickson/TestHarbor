using NUnit.Framework;
using TestHarbor.NUnit;

namespace TestHarbor.Tests;

/// <summary>Verifies fresh sessions and complete cleanup across NUnit repeat and retry invocations.</summary>
public sealed class RepeatRetryTests : HarborFixture
{
    /// <summary>Counts retry attempts across NUnit-created fixture instances.</summary>
    private static int attempts;
    /// <summary>Counts ready session observations across invocations.</summary>
    private static int starts;
    /// <summary>Counts completed session observations across invocations.</summary>
    private static int stops;
    /// <summary>Records lifecycle evidence for every repeated or retried invocation.</summary>
    /// <value>Session observer options.</value>
    protected override TestSessionOptions SessionOptions => new() { Observer = observation =>
    {
        if (observation.Phase == SessionPhase.Ready) Interlocked.Increment(ref starts);
        if (observation.Phase == SessionPhase.Finished) Interlocked.Increment(ref stops);
    }};
    /// <summary>Executes three successful invocations with independent sessions.</summary>
    [Test, Repeat(3)]
    public void Repeated() => Assert.That(Services, Is.Not.Null);
    /// <summary>Fails once and succeeds on retry, requiring cleanup before reinitialization.</summary>
    [Test, Retry(2)]
    public void Retried() => Assert.That(Interlocked.Increment(ref attempts), Is.EqualTo(2));
    /// <summary>Verifies each invocation completed its session exactly once.</summary>
    [OneTimeTearDown]
    public static void VerifyCounts()
    {
        Assert.That(starts, Is.Positive);
        Assert.That(stops, Is.EqualTo(starts));
    }
}
