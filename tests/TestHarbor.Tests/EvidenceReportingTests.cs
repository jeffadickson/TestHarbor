using NUnit.Framework;
using TestHarbor.NUnit;

namespace TestHarbor.Tests;

/// <summary>Verifies retained artifact and lifecycle evidence can be attached through the real NUnit adapter.</summary>
public sealed class EvidenceReportingTests
{
    /// <summary>Publishes redacted evidence and a phase journal for external report verification.</summary>
    /// <returns>A task completing after artifacts are attached to the current test.</returns>
    [Test]
    public async Task PublishesRetainedEvidence()
    {
        var directory = Environment.GetEnvironmentVariable("TESTHARBOR_EVIDENCE_OUTPUT")
            ?? Path.Combine(TestContext.CurrentContext.WorkDirectory, "TestResults", "evidence", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var store = new ArtifactStore(directory);
        var evidence = await store.PublishTextAsync("token=private-value", "evidence.txt", TestContext.CurrentContext.Test.ID, ["private-value"]);
        HarborArtifacts.Attach(evidence);
        var journalPath = Path.Combine(directory, "lifecycle.jsonl");
        using (var journal = new LifecycleJournal(journalPath))
            await TestSession.RunAsync([], (_, _) => Task.CompletedTask, options: new() { Observer = journal.Record });
        using var journalInput = File.OpenRead(journalPath);
        HarborArtifacts.Attach(await store.PublishAsync(journalInput, "lifecycle.jsonl", TestContext.CurrentContext.Test.ID, "application/x-ndjson"));
        Assert.That(File.ReadAllText(evidence.Path), Does.Not.Contain("private-value"));
    }
}
