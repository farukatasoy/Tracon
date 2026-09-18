using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// The compiled-agent cache key measures a definition's CONTENT. These tests
/// hold the property the version number lost the moment a name could be
/// deleted and recreated (HATA-S4-004).
/// </summary>
public sealed class DefinitionFingerprintTests
{
    [Fact]
    public void Identical_content_produces_the_same_fingerprint()
    {
        AgentDefinitionCompiler.CreateDefinitionFingerprint(TestData.Definition("a"))
            .ShouldBe(AgentDefinitionCompiler.CreateDefinitionFingerprint(TestData.Definition("a")));
    }

    [Fact]
    public void A_recreated_name_at_version_one_does_not_reuse_the_deleted_definitions_entry()
    {
        // The exact shape of the defect: both definitions are version 1, both
        // carry the same name, and only the content differs. Keyed by version
        // the two were indistinguishable.
        var deleted = TestData.Definition("a") with { Version = 1, Instructions = "old" };
        var recreated = TestData.Definition("a") with { Version = 1, Instructions = "new" };

        AgentDefinitionCompiler.CreateDefinitionFingerprint(recreated)
            .ShouldNotBe(AgentDefinitionCompiler.CreateDefinitionFingerprint(deleted), StringComparer.Ordinal);
    }

    [Fact]
    public void A_changed_model_binding_changes_the_fingerprint()
    {
        // The manual-test case that found this: the recreated agent named a
        // different model and the run kept calling the deleted one.
        var before = TestData.Definition("a") with { Model = new ModelBinding { Provider = "fake", Model = "model-one" } };
        var after = TestData.Definition("a") with { Model = new ModelBinding { Provider = "fake", Model = "model-two" } };

        AgentDefinitionCompiler.CreateDefinitionFingerprint(after)
            .ShouldNotBe(AgentDefinitionCompiler.CreateDefinitionFingerprint(before), StringComparer.Ordinal);
    }

    [Fact]
    public void Every_field_of_the_record_reaches_the_fingerprint()
    {
        // Structural guard. The fingerprint hashes the serialized record rather
        // than a hand-written field list precisely so that a field added to
        // AgentDefinition later cannot be forgotten here. This test fails if
        // someone replaces the serialization with such a list: it walks the
        // record's own properties and demands that changing any one of them
        // changes the fingerprint.
        var baseline = TestData.Definition("a");
        var baselineFingerprint = AgentDefinitionCompiler.CreateDefinitionFingerprint(baseline);

        foreach (var mutated in Mutations(baseline))
        {
            AgentDefinitionCompiler.CreateDefinitionFingerprint(mutated.Definition)
                .ShouldNotBe(baselineFingerprint, StringComparer.Ordinal, $"changing {mutated.Field} left the fingerprint unchanged");
        }
    }

    private static IEnumerable<(string Field, AgentDefinition Definition)> Mutations(AgentDefinition baseline)
    {
        yield return (nameof(AgentDefinition.Name), baseline with { Name = "other-name" });
        yield return (nameof(AgentDefinition.DisplayName), baseline with { DisplayName = "Other" });
        yield return (nameof(AgentDefinition.Description), baseline with { Description = "Other." });
        yield return (nameof(AgentDefinition.Instructions), baseline with { Instructions = "Other." });
        yield return (
            nameof(AgentDefinition.InstructionsByCulture),
            baseline with { InstructionsByCulture = new Dictionary<string, string>(StringComparer.Ordinal) { ["tr"] = "Merhaba." } });
        yield return (nameof(AgentDefinition.Model), baseline with { Model = new ModelBinding { Provider = "fake", Model = "other" } });
        yield return (nameof(AgentDefinition.ToolNames), baseline with { ToolNames = ["a_tool"] });
        yield return (nameof(AgentDefinition.SkillNames), baseline with { SkillNames = ["a_skill"] });
        yield return (nameof(AgentDefinition.CallableAgentNames), baseline with { CallableAgentNames = ["other-agent"] });
        yield return (nameof(AgentDefinition.SubAgents), baseline with { SubAgents = new SubAgentSettings { ChildDeadline = TimeSpan.FromSeconds(7) } });
        yield return (nameof(AgentDefinition.McpResourceUris), baseline with { McpResourceUris = ["server:res://a"] });
        yield return (nameof(AgentDefinition.Harness), baseline with { Harness = new HarnessSettings() });
        yield return (nameof(AgentDefinition.Compaction), baseline with { Compaction = new CompactionSettings() });
        yield return (nameof(AgentDefinition.Memory), baseline with { Memory = new MemorySettings() });
        yield return (nameof(AgentDefinition.Origin), baseline with { Origin = AgentDefinitionOrigin.Code });
        yield return (nameof(AgentDefinition.Version), baseline with { Version = baseline.Version + 1 });
        yield return (nameof(AgentDefinition.TenantId), baseline with { TenantId = "other-tenant" });
        yield return (nameof(AgentDefinition.UpdatedAt), baseline with { UpdatedAt = DateTimeOffset.UnixEpoch });
        yield return (
            nameof(AgentDefinition.Metadata),
            baseline with { Metadata = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal) { ["k"] = System.Text.Json.JsonDocument.Parse("1").RootElement } });
        yield return (
            nameof(AgentDefinition.Parameters),
            baseline with { Parameters = [new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text }] });
        yield return (nameof(AgentDefinition.SharedInstructionsName), baseline with { SharedInstructionsName = "house-rules" });
    }
}
