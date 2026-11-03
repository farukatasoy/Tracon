using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Generated = Tracon.Client.Generated;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A caller that sets only the properties the document marks required gets a working request through the generated
/// client.
/// </summary>
/// <remarks>
/// The document says a free-form member (<c>providerSettings</c>, <c>metadata</c>, <c>checks</c>, <c>payload</c>) is
/// optional and accepts <c>null</c>, so the generated type is nullable and the client leaves it unset. A member that
/// has a server default carries that default in the generated type. Before that, the first request of a caller that
/// set only the required members failed with a <c>400</c> the server's contract gives for a null, or with an
/// <see cref="InvalidOperationException"/> from the serializer.
/// </remarks>
public sealed class GeneratedClientMinimalRequestTests
{
    [Fact]
    public async Task An_agent_created_with_only_a_name_and_a_model_is_accepted()
    {
        await using var host = await OpenApiSnapshotTests.StartFullSurfaceHostAsync();
        var client = Client(host);

        var created = await client.TraconCreateAgentAsync(
            new Generated.AgentDefinitionRequest
            {
                Name = "minimal-agent",
                Model = new Generated.ModelBinding { Provider = "echo", Model = "echo-1" },
            });

        created.Name.ShouldBe("minimal-agent");
    }

    [Fact]
    public async Task Provider_settings_of_the_value_types_the_client_knows_reach_the_server_and_come_back()
    {
        await using var host = await OpenApiSnapshotTests.StartFullSurfaceHostAsync();
        var client = Client(host);

        var created = await client.TraconCreateAgentAsync(
            new Generated.AgentDefinitionRequest
            {
                Name = "settings-agent",
                Model = new Generated.ModelBinding
                {
                    Provider = "echo",
                    Model = "echo-1",
                    ProviderSettings = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["anthropic.promptCaching"] = true,
                        ["anthropic.maxTokens"] = 8000,
                        ["note"] = "text",
                        ["ratio"] = 0.5,
                        ["tags"] = new[] { "a", "b" },
                        ["nested"] = new Dictionary<string, object>(StringComparer.Ordinal) { ["depth"] = 2 },
                    },
                },
            });

        var settings = created.Model.ProviderSettings.ShouldNotBeNull();

        System.Text.Json.JsonSerializer.Serialize(settings["anthropic.promptCaching"]).ShouldBe("true");
        System.Text.Json.JsonSerializer.Serialize(settings["anthropic.maxTokens"]).ShouldBe("8000");
        System.Text.Json.JsonSerializer.Serialize(settings["note"]).ShouldBe("\"text\"");
        System.Text.Json.JsonSerializer.Serialize(settings["ratio"]).ShouldBe("0.5");
        System.Text.Json.JsonSerializer.Serialize(settings["tags"]).ShouldBe("[\"a\",\"b\"]");
        System.Text.Json.JsonSerializer.Serialize(settings["nested"]).ShouldBe("{\"depth\":2}");
    }

    [Fact]
    public async Task A_provider_setting_of_a_type_the_client_has_no_metadata_for_is_refused_before_the_request_is_sent()
    {
        // The client serializes with a generated context, so a value is written only if its runtime type is in that
        // context. The README names the types that are; a value of another type is passed as a JsonElement.
        await using var host = await OpenApiSnapshotTests.StartFullSurfaceHostAsync();
        var client = Client(host);

        var request = new Generated.AgentDefinitionRequest
        {
            Name = "unknown-type-agent",
            Model = new Generated.ModelBinding
            {
                Provider = "echo",
                Model = "echo-1",
                ProviderSettings = new Dictionary<string, object>(StringComparer.Ordinal) { ["price"] = 1.5m },
            },
        };

        await Should.ThrowAsync<NotSupportedException>(() => client.TraconCreateAgentAsync(request));
    }

    [Fact]
    public async Task A_skill_saved_with_only_its_required_properties_is_accepted()
    {
        await using var host = await OpenApiSnapshotTests.StartFullSurfaceHostAsync();
        var client = Client(host);

        var saved = await client.TraconSaveSkillAsync(
            "minimal-skill",
            new Generated.AgentSkillRequest { Name = "minimal-skill", Description = "A skill.", Instructions = "Do the thing." });

        saved.Name.ShouldBe("minimal-skill");
    }

    [Fact]
    public async Task An_eval_suite_saved_without_checks_is_accepted()
    {
        await using var host = await OpenApiSnapshotTests.StartFullSurfaceHostAsync(configureMoreTracon: static b => b.AddAgent(TestData.Definition()));
        var client = Client(host);

        var saved = await client.TraconSaveEvalSuiteAsync("minimal-suite", new Generated.EvalSuiteSaveRequest { AgentName = "kod-agent" });

        saved.Name.ShouldBe("minimal-suite");
    }

    private static Generated.TraconApiClient Client(TraconTestHost host)
    {
        host.Client.BaseAddress = new Uri("http://localhost/tracon/");

        return new Generated.TraconApiClient(host.Client);
    }
}
