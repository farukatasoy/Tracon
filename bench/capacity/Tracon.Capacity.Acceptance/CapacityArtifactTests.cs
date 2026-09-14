using Tracon.CapacityDriver;

namespace Tracon.Capacity.Acceptance;

/// <summary>Nothing the run wrote may carry a credential.</summary>
/// <remarks>
/// 🚨 The orchestrator plants a synthetic canary credential in the host's
/// environment before this suite runs. A scan that never sees a credential
/// proves nothing about a scan that would; the canary is what makes these
/// cases falsifiable.
/// </remarks>
public sealed class CapacityArtifactTests
{
    private static IEnumerable<string> ArtifactFiles()
        => Directory.EnumerateFiles(AcceptanceEnvironment.ArtifactDirectory, "*", SearchOption.AllDirectories)
            .Where(static path => path.EndsWith(".json", StringComparison.Ordinal)
                               || path.EndsWith(".md", StringComparison.Ordinal)
                               || path.EndsWith(".jsonl", StringComparison.Ordinal)
                               || path.EndsWith(".svg", StringComparison.Ordinal));

    [Fact]
    public void The_planted_canary_is_something_the_scan_would_actually_catch()
    {
        // If this fails, every other case in this class is vacuous.
        Redactor.ContainsSecret(AcceptanceEnvironment.Canary).ShouldBeTrue();
    }

    [Fact]
    public void No_artifact_carries_the_canary()
    {
        var offenders = ArtifactFiles()
            .Where(path => File.ReadAllText(path).Contains(AcceptanceEnvironment.Canary, StringComparison.Ordinal))
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void No_artifact_carries_any_credential_shape_at_all()
    {
        var offenders = ArtifactFiles()
            .Where(static path => Redactor.ContainsSecret(File.ReadAllText(path)))
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void The_manifest_carries_no_connection_string()
    {
        var manifest = Path.Combine(AcceptanceEnvironment.ArtifactDirectory, "manifest.json");

        File.Exists(manifest).ShouldBeTrue();

        var text = File.ReadAllText(manifest);

        text.ShouldNotContain("Password=", Case.Sensitive);
        text.ShouldNotContain("ConnectionString", Case.Sensitive);
        Redactor.ContainsSecret(text).ShouldBeFalse();
    }

    [Fact]
    public void The_manifest_carries_the_frame_every_number_must_travel_with()
    {
        var text = File.ReadAllText(Path.Combine(AcceptanceEnvironment.ArtifactDirectory, "manifest.json"));

        text.ShouldContain("not an SLA");
        text.ShouldContain(AcceptanceEnvironment.PackageVersion);
    }
}
