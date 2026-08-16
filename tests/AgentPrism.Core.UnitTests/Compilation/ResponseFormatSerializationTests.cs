using System.Text.Json;

namespace AgentPrism.Core.UnitTests.Compilation;

/// <summary>
/// Old records do not carry a <c>responseFormat</c> key (definitions written before
/// this phase). Deserialization must produce <see langword="null"/>, not throw.
/// </summary>
public sealed class ResponseFormatSerializationTests
{
    [Fact]
    public void Old_json_without_ResponseFormat_key_produces_null()
    {
        const string json = """{"provider":"openai","model":"gpt-5"}""";

        var binding = JsonSerializer.Deserialize<ModelBinding>(json, JsonSerializerOptions.Web);

        binding.ShouldNotBeNull();
        binding!.ResponseFormat.ShouldBeNull();
    }

    [Fact]
    public void Old_AgentDefinition_json_is_read_with_source_generated_context()
    {
        // AgentPrismCoreJsonContext is the real path written to the jsonb column
        // of AgentPrism.PostgreSql/SqlServer/Sqlite (decision K-006). The JSON of a
        // row written before this phase never has a "responseFormat" key.
        const string json = """
            {
              "name": "old-agent",
              "instructions": "test",
              "model": { "provider": "openai", "model": "gpt-5" },
              "toolNames": [],
              "skillNames": [],
              "callableAgentNames": []
            }
            """;

        var definition = JsonSerializer.Deserialize(json, AgentPrismCoreJsonContext.Default.AgentDefinition);

        definition.ShouldNotBeNull();
        definition!.Model.ResponseFormat.ShouldBeNull();
    }
}
