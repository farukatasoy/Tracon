using System.Net.Http.Json;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// An earlier fix end to end: an agent run through the whole host — compiled agent,
/// Microsoft Agent Framework, the model pipeline and the real Anthropic adapter —
/// sends the <c>anthropic.*</c> settings in the request body.
/// </summary>
/// <remarks>
/// The provider-level body tests (<c>Tracon.Anthropic.UnitTests.AnthropicRequestSettingsTests</c>)
/// call the client with hand-made <c>ChatOptions</c>. Only this test proves
/// that the agent's instructions reach the provider as
/// <c>ChatOptions.Instructions</c> — the field the <c>system</c> placement marks —
/// and that the compiled response format and the effort share
/// <c>output_config</c> in a real run.
/// </remarks>
public sealed class AnthropicRequestSettingsEndpointTests
{
    private const string AgentName = "anthropic-settings-agent";

    [Fact]
    public async Task A_run_sends_effort_with_the_schema_and_marks_the_instructions_for_a_one_hour_cache()
    {
        await using var anthropic = await FakeAnthropicServer.StartAsync();

        await using var host = await TraconTestHost.StartAsync(configureTracon: builder => builder
            .UseAnthropic("test-anthropic-request-settings", options =>
            {
                options.Endpoint = anthropic.BaseAddress;
                options.MaxRetries = 0;
                options.Models.Add(new ModelDescriptor { Name = "claude-test", SupportsStructuredOutput = true });
            })
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "You write podcast scripts as JSON.",
                Model = new ModelBinding
                {
                    Provider = AnthropicProviderNames.Anthropic,
                    Model = "claude-test",
                    MaxOutputTokens = 64_000,
                    ResponseFormat = new AgentResponseFormat
                    {
                        Kind = AgentResponseFormatKind.JsonSchema,
                        Schema = JsonDocument.Parse(
                            """{"type":"object","properties":{"answer":{"type":"integer"}},"required":["answer"]}""").RootElement,
                        SchemaName = "script",
                    },
                    ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        [AnthropicProviderNames.EffortSetting] = JsonSerializer.SerializeToElement("high"),
                        [AnthropicProviderNames.ThinkingModeSetting] = JsonSerializer.SerializeToElement("adaptive"),
                        [AnthropicProviderNames.PromptCachingSetting] = JsonSerializer.SerializeToElement(true),
                        [AnthropicProviderNames.PromptCachingTtlSetting] = JsonSerializer.SerializeToElement("1h"),
                        [AnthropicProviderNames.PromptCachingPlacementSetting] = JsonSerializer.SerializeToElement("system"),
                    },
                },
                Origin = AgentDefinitionOrigin.Code,
            }));

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/agents/{AgentName}/run")
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "Article: the first document." }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        var body = anthropic.Bodies.ShouldHaveSingleItem();

        body["max_tokens"]!.GetValue<int>().ShouldBe(64_000);
        body["output_config"]!["effort"]!.GetValue<string>().ShouldBe("high");
        body["output_config"]!["format"]!["type"]!.GetValue<string>().ShouldBe("json_schema");
        body["thinking"]!["type"]!.GetValue<string>().ShouldBe("adaptive");
        body.ContainsKey("cache_control").ShouldBeFalse("the system placement sends no top-level breakpoint");

        var lastSystemBlock = body["system"]!.AsArray()[^1]!.AsObject();

        lastSystemBlock["text"]!.GetValue<string>().ShouldContain("You write podcast scripts as JSON.");
        lastSystemBlock["cache_control"]!["ttl"]!.GetValue<string>().ShouldBe("1h");

        // The document changes per request; it must stay AFTER the breakpoint.
        body["messages"]!.ToJsonString().ShouldContain("the first document");
        lastSystemBlock.ToJsonString().ShouldNotContain("the first document");
    }
}
