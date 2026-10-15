using NUnit.Framework;

namespace TestHarbor.NUnit;

/// <summary>Attaches retained evidence through NUnit's public reporting API.</summary>
public static class HarborArtifacts
{
    /// <summary>Attaches an existing artifact and its provenance sidecar to the current test.</summary>
    /// <param name="evidence">Published retained artifact.</param>
    /// <remarks>Keep files alive until the runner finishes collecting attachments. This does not convert
    /// NUnit XML/TRX to JUnit reporting formats.</remarks>
    public static void Attach(ArtifactEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        TestContext.AddTestAttachment(evidence.Path, evidence.MediaType);
        TestContext.AddTestAttachment(evidence.Path + ".json", "TestHarbor provenance");
    }
}
