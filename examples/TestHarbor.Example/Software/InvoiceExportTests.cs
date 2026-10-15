using System.Globalization;
using System.Text.Json;
using NUnit.Framework;
using TestHarbor.NUnit;

namespace TestHarbor.Example.Software;

/// <summary>Tests a real JSON-to-text export with isolated working files and retained diagnostic evidence.</summary>
[Parallelizable(ParallelScope.All)]
public sealed class InvoiceExportTests
{
    /// <summary>Checks valid exports and verifies successful session cleanup removes the working directory.</summary>
    /// <param name="amount">Invoice amount in whole currency units.</param>
    /// <param name="expected">Expected formatted invoice text.</param>
    /// <returns>A task completing after export and workspace cleanup.</returns>
    [TestCase(25, "Invoice total: 25.00")]
    [TestCase(100, "Invoice total: 100.00")]
    public async Task ExportsInvoiceAndDeletesScratchFiles(int amount, string expected)
    {
        string? workspace = null;
        ArtifactEvidence? evidence = null;
        await TestSession.RunAsync([], async (session, token) =>
        {
            workspace = session.CreateTemporaryDirectory().Path;
            var input = Path.Combine(workspace, "input.json");
            var output = Path.Combine(workspace, "invoice.txt");
            await File.WriteAllTextAsync(input, JsonSerializer.Serialize(amount), token);
            await InvoiceExporter.ExportAsync(input, output, token);
            var text = await File.ReadAllTextAsync(output, token);
            Assert.That(text, Is.EqualTo(expected));
            evidence = await EvidenceStore().PublishTextAsync(text, "invoice.txt", "invoice-success", cancellationToken: token);
            HarborArtifacts.Attach(evidence);
        });
        Assert.That(Directory.Exists(workspace), Is.False);
        Assert.That(File.Exists(evidence!.Path), Is.True);
        TestContext.Out.WriteLine("[Software/files] TestHarbor deleted the successful test's isolated scratch directory. The exported invoice and provenance were copied to retained evidence for NUnit attachments. No named lock was necessary because each test uses unique files.");
    }

    /// <summary>Verifies failure keeps its input workspace long enough to capture sanitized diagnostics.</summary>
    /// <returns>A task completing after expected failure, evidence publication and explicit retained-workspace removal.</returns>
    [Test]
    public async Task FailedExportRetainsReproducibleInput()
    {
        string? workspace = null;
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(async () => await TestSession.RunAsync([], async (session, token) =>
            {
                workspace = session.CreateTemporaryDirectory().Path;
                var input = Path.Combine(workspace, "input.json");
                await File.WriteAllTextAsync(input, "-1", token);
                await InvoiceExporter.ExportAsync(input, Path.Combine(workspace, "invoice.txt"), token);
            }));
            Assert.That(Directory.Exists(workspace), Is.True);
            Assert.That(await File.ReadAllTextAsync(Path.Combine(workspace!, "input.json")), Is.EqualTo("-1"));
            var diagnostic = "Input: -1\nReason: negative invoice amount\nToken: example-secret";
            var evidence = await EvidenceStore().PublishTextAsync(diagnostic, "failed-export.txt",
                "invoice-failure", ["example-secret"]);
            Assert.That(await File.ReadAllTextAsync(evidence.Path), Does.Not.Contain("example-secret"));
            HarborArtifacts.Attach(evidence);
            TestContext.Out.WriteLine("[Software/failure evidence] RunAsync observed the export failure and retained its workspace. The test copied a redacted diagnostic to persistent evidence, then explicitly removed retained scratch files. NUnit verifies the expected exception.");
        }
        finally
        {
            // Retained directories belong to the caller once the failed session has closed.
            if (workspace is not null && Directory.Exists(workspace)) Directory.Delete(workspace, true);
        }
    }

    /// <summary>Creates a retained evidence destination collected by CI without sharing output paths between cases.</summary>
    /// <returns>A bounded artifact publisher rooted outside temporary session directories.</returns>
    private static ArtifactStore EvidenceStore() => new(Path.Combine(TestContext.CurrentContext.WorkDirectory,
        "TestResults", "software-evidence", Guid.NewGuid().ToString("N")));
}

/// <summary>Implements a small file-processing application without depending on TestHarbor.</summary>
public static class InvoiceExporter
{
    /// <summary>Reads a JSON amount, validates it and writes an invariant-culture invoice.</summary>
    /// <param name="input">JSON file containing a decimal amount.</param>
    /// <param name="output">Destination text file.</param>
    /// <param name="token">Cooperative file-operation cancellation.</param>
    /// <returns>A task completing after the invoice is written.</returns>
    public static async Task ExportAsync(string input, string output, CancellationToken token)
    {
        var amount = JsonSerializer.Deserialize<decimal>(await File.ReadAllTextAsync(input, token));
        if (amount < 0) throw new InvalidDataException("Invoice amount cannot be negative.");
        await File.WriteAllTextAsync(output, "Invoice total: " + amount.ToString("F2", CultureInfo.InvariantCulture), token);
    }
}
