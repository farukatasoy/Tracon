using System.Text.Json;

namespace AgentPrism.Core.UnitTests.Architecture;

/// <summary>Protects the assembly-based DocFX metadata input from duplicate references.</summary>
public sealed class DocfxConfigurationTests
{
    [Fact]
    public void Assembly_metadata_does_not_reload_artifact_outputs_as_references()
    {
        var path = Path.Combine(CapabilityEntryPoints.RepositoryRoot, "docfx", "docfx.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var metadataEntries = document.RootElement.GetProperty("metadata").EnumerateArray();

        foreach (var metadata in metadataEntries)
        {
            metadata.TryGetProperty("references", out _).ShouldBeFalse(
                "The explicit src assemblies already resolve their dependencies through their .deps.json files. " +
                "A references glob over artifacts/bin reloads the same AgentPrism assembly from every test and " +
                "sample output directory, which makes docfx metadata fail with CS1704.");
        }
    }
}
