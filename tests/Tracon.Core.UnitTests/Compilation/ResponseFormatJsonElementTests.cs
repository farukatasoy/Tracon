using System.Text.Json;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// Verifies the struct trap from MEMORY.md for <see cref="AgentResponseFormat.Schema"/>:
/// an unassigned <see cref="JsonElement"/> field stays <c>default</c>, which is
/// <c>ValueKind = Undefined</c>, and serialization then collapses the ENTIRE
/// list containing it. Because the contract makes <c>Schema</c> a
/// <c>JsonElement?</c>, the unassigned field is <see langword="null"/>, not
/// <c>Undefined</c>.
/// </summary>
public sealed class ResponseFormatJsonElementTests
{
    [Fact]
    public void AgentResponseFormat_with_an_unassigned_schema_does_not_collapse_the_list()
    {
        var bindings = new List<ModelBinding>
        {
            new() { Provider = "openai", Model = "gpt-5" },
            new()
            {
                Provider = "openai",
                Model = "gpt-5",
                ResponseFormat = new AgentResponseFormat { Kind = AgentResponseFormatKind.Text },
            },
            new() { Provider = "anthropic", Model = "claude-sonnet-5" },
        };

        var json = JsonSerializer.Serialize(bindings);
        var roundTripped = JsonSerializer.Deserialize<List<ModelBinding>>(json);

        roundTripped.ShouldNotBeNull();
        roundTripped!.Count.ShouldBe(3);
        roundTripped[1].ResponseFormat.ShouldNotBeNull();
        roundTripped[1].ResponseFormat!.Schema.ShouldBeNull();
    }

    [Fact]
    public void Empty_AgentResponseFormat_produces_a_null_schema_on_serialization()
    {
        var format = new AgentResponseFormat { Kind = AgentResponseFormatKind.Json };

        var json = JsonSerializer.Serialize(format);
        var roundTripped = JsonSerializer.Deserialize<AgentResponseFormat>(json);

        roundTripped.ShouldNotBeNull();
        roundTripped!.Schema.ShouldBeNull();
    }
}
