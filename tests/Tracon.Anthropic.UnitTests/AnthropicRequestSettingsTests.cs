using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Microsoft.Extensions.AI;
using Tracon.Anthropic.UnitTests.Infrastructure;
using Tracon.ProviderCore.Tests;

namespace Tracon.Anthropic.UnitTests;

/// <summary>
/// F-346 — what each <c>anthropic.*</c> setting puts in the request body, read
/// from the body the SDK actually sends (raw capture), and the settings a
/// tenant's own credential (BYOK) client must carry too.
/// </summary>
public sealed class AnthropicRequestSettingsTests
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """{"type":"object","properties":{"answer":{"type":"integer"}},"required":["answer"]}""").RootElement.Clone();

    [Fact]
    public async Task Effort_is_sent_as_output_config_effort()
    {
        var body = await SendAsync(Settings((AnthropicProviderNames.EffortSetting, "high")));

        body["output_config"]!["effort"]!.GetValue<string>().ShouldBe("high");
        body.ContainsKey("thinking").ShouldBeFalse("effort alone does not switch thinking on");
    }

    [Fact]
    public async Task Effort_and_a_structured_response_share_output_config_without_overwriting_each_other()
    {
        // 🚨 Two writers fill output_config: this provider (effort) and the
        // adapter (format, from ChatOptions.ResponseFormat). Measured: the
        // adapter copies an existing OutputConfig and sets only Format.
        var body = await SendAsync(
            Settings((AnthropicProviderNames.EffortSetting, "xhigh")),
            new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(Schema, "answer") });

        var outputConfig = body["output_config"]!.AsObject();

        outputConfig["effort"]!.GetValue<string>().ShouldBe("xhigh");
        outputConfig["format"]!["type"]!.GetValue<string>().ShouldBe("json_schema");
        outputConfig["format"]!["schema"]!["properties"]!["answer"].ShouldNotBeNull();
    }

    [Fact]
    public async Task Effort_values_come_from_the_SDK_enum_and_are_matched_case_insensitively()
    {
        AnthropicRequestSettings.EffortValues.ShouldBe(["low", "medium", "high", "xhigh", "max"]);

        var body = await SendAsync(Settings((AnthropicProviderNames.EffortSetting, "MAX")));

        body["output_config"]!["effort"]!.GetValue<string>().ShouldBe("max");
    }

    [Fact]
    public async Task Request_placement_sends_a_top_level_cache_control_with_the_ttl()
    {
        var body = await SendAsync(Settings(
            (AnthropicProviderNames.PromptCachingSetting, true),
            (AnthropicProviderNames.PromptCachingTtlSetting, "1h")));

        body["cache_control"]!["type"]!.GetValue<string>().ShouldBe("ephemeral");
        body["cache_control"]!["ttl"]!.GetValue<string>().ShouldBe("1h");
    }

    [Fact]
    public async Task Prompt_caching_without_a_ttl_sends_no_ttl_field()
    {
        // Today's request, unchanged: Anthropic's own default (5 minutes) applies.
        var body = await SendAsync(Settings((AnthropicProviderNames.PromptCachingSetting, true)));

        body["cache_control"]!.AsObject().ContainsKey("ttl").ShouldBeFalse();
    }

    [Fact]
    public async Task System_placement_marks_the_end_of_the_instructions_and_sends_no_top_level_cache_control()
    {
        var body = await SendAsync(
            Settings(
                (AnthropicProviderNames.PromptCachingSetting, true),
                (AnthropicProviderNames.PromptCachingTtlSetting, "1h"),
                (AnthropicProviderNames.PromptCachingPlacementSetting, "system")),
            new ChatOptions { Instructions = "You write podcast scripts." });

        body.ContainsKey("cache_control").ShouldBeFalse();

        var system = body["system"]!.AsArray();
        var last = system[^1]!.AsObject();

        last["text"]!.GetValue<string>().ShouldBe("You write podcast scripts.");
        last["cache_control"]!["type"]!.GetValue<string>().ShouldBe("ephemeral");
        last["cache_control"]!["ttl"]!.GetValue<string>().ShouldBe("1h");
    }

    [Fact]
    public async Task System_placement_keeps_the_instructions_after_the_callers_system_messages()
    {
        // Today the adapter puts system-role messages first and the
        // instructions last; marking the instructions must not reorder them.
        var handler = new CapturingAnthropicHandler();
        using var client = Client(handler, Settings(
            (AnthropicProviderNames.PromptCachingSetting, true),
            (AnthropicProviderNames.PromptCachingPlacementSetting, "system")));

        List<ChatMessage> messages =
        [
            new(ChatRole.System, "Context from memory."),
            new(ChatRole.User, "hi"),
        ];

        await client.GetResponseAsync(
            messages, new ChatOptions { Instructions = "The instructions." }, TestContext.Current.CancellationToken);

        var system = handler.Last["system"]!.AsArray();

        system.Count.ShouldBe(2);
        system[0]!["text"]!.GetValue<string>().ShouldBe("Context from memory.");
        system[0]!.AsObject().ContainsKey("cache_control").ShouldBeFalse();
        system[1]!["text"]!.GetValue<string>().ShouldBe("The instructions.");
        system[1]!["cache_control"].ShouldNotBeNull();

        messages.Count.ShouldBe(2, "the caller's message list is never mutated");
    }

    [Fact]
    public async Task System_placement_without_instructions_falls_back_to_the_top_level_breakpoint()
    {
        var body = await SendAsync(Settings(
            (AnthropicProviderNames.PromptCachingSetting, true),
            (AnthropicProviderNames.PromptCachingPlacementSetting, "system")));

        body["cache_control"]!["type"]!.GetValue<string>().ShouldBe("ephemeral");
    }

    [Fact]
    public async Task Adaptive_thinking_sends_the_adaptive_type_and_no_budget()
    {
        var body = await SendAsync(Settings((AnthropicProviderNames.ThinkingModeSetting, "adaptive")));

        var thinking = body["thinking"]!.AsObject();

        thinking["type"]!.GetValue<string>().ShouldBe("adaptive");
        thinking.ContainsKey("budget_tokens").ShouldBeFalse();
    }

    [Fact]
    public async Task A_budget_alone_still_sends_enabled_thinking_as_before()
    {
        var body = await SendAsync(Settings((AnthropicProviderNames.ThinkingBudgetTokensSetting, 2048)));

        body["thinking"]!["type"]!.GetValue<string>().ShouldBe("enabled");
        body["thinking"]!["budget_tokens"]!.GetValue<int>().ShouldBe(2048);
    }

    [Theory]
    [InlineData(ReasoningEffort.Low, 1024)]
    [InlineData(ReasoningEffort.High, 16384)]
    [InlineData(ReasoningEffort.ExtraHigh, 32768)]
    public async Task ReasoningEffort_alone_becomes_an_enabled_thinking_budget(ReasoningEffort effort, int budgetTokens)
    {
        // The SDK adapter's mapping, read from the body it sends. The guide and
        // the README state these numbers; this keeps them honest across an SDK
        // update.
        var body = await SendAsync(
            Settings(),
            new ChatOptions { MaxOutputTokens = 64000, Reasoning = new ReasoningOptions { Effort = effort } });

        body["thinking"]!["type"]!.GetValue<string>().ShouldBe("enabled");
        body["thinking"]!["budget_tokens"]!.GetValue<int>().ShouldBe(budgetTokens);
        body.ContainsKey("output_config").ShouldBeFalse("ReasoningEffort is not anthropic.effort");
    }

    [Fact]
    public async Task A_thinking_setting_wins_over_ReasoningEffort()
    {
        var body = await SendAsync(
            Settings((AnthropicProviderNames.ThinkingModeSetting, "adaptive")),
            new ChatOptions { MaxOutputTokens = 64000, Reasoning = new ReasoningOptions { Effort = ReasoningEffort.High } });

        var thinking = body["thinking"]!.AsObject();

        thinking["type"]!.GetValue<string>().ShouldBe("adaptive");
        thinking.ContainsKey("budget_tokens").ShouldBeFalse("the adapter skips its own mapping when thinking is already set");
    }

    [Fact]
    public async Task No_setting_sends_none_of_the_fields()
    {
        var body = await SendAsync(Settings());

        body.ContainsKey("output_config").ShouldBeFalse();
        body.ContainsKey("cache_control").ShouldBeFalse();
        body.ContainsKey("thinking").ShouldBeFalse();
        body["max_tokens"]!.GetValue<int>().ShouldBe(4096, "no ChatOptions.MaxOutputTokens: the factory default applies");
    }

    [Theory]
    [InlineData(AnthropicProviderNames.EffortSetting, "extreme")]
    [InlineData(AnthropicProviderNames.PromptCachingPlacementSetting, "tools")]
    [InlineData(AnthropicProviderNames.ThinkingModeSetting, "disabled")]
    public void A_wrong_value_is_rejected_and_names_the_key_and_the_valid_values(string key, string value)
    {
        var settings = Settings((AnthropicProviderNames.PromptCachingSetting, true), (key, value));

        var exception = Should.Throw<TraconException>(() => Factory().CreateChatClient(TestData.Binding(providerSettings: settings)));

        exception.Message.ShouldContain(key);
        exception.Message.ShouldContain("Valid values");

        // Phase 203: the rejected value is never repeated (it can be a credential pasted
        // into the wrong field); the key and the valid values locate the mistake.
        exception.Message.ShouldNotContain(value);
    }

    [Fact]
    public void A_wrong_ttl_is_rejected_with_the_SDK_values()
    {
        var exception = Should.Throw<TraconException>(() => Factory().CreateChatClient(TestData.Binding(providerSettings: Settings(
            (AnthropicProviderNames.PromptCachingSetting, true),
            (AnthropicProviderNames.PromptCachingTtlSetting, "2h")))));

        exception.Message.ShouldContain(AnthropicProviderNames.PromptCachingTtlSetting);
        exception.Message.ShouldContain("5m, 1h");
    }

    [Theory]
    [InlineData(AnthropicProviderNames.PromptCachingTtlSetting, "1h")]
    [InlineData(AnthropicProviderNames.PromptCachingPlacementSetting, "system")]
    public void A_cache_setting_without_prompt_caching_is_an_error_not_a_silent_no_op(string key, string value)
    {
        var exception = Should.Throw<TraconException>(() => Factory().CreateChatClient(
            TestData.Binding(providerSettings: Settings((key, value)))));

        exception.Message.ShouldContain(key);
        exception.Message.ShouldContain(AnthropicProviderNames.PromptCachingSetting);
    }

    [Fact]
    public void Enabled_thinking_without_a_budget_is_an_error()
    {
        var exception = Should.Throw<TraconException>(() => Factory().CreateChatClient(
            TestData.Binding(providerSettings: Settings((AnthropicProviderNames.ThinkingModeSetting, "enabled")))));

        exception.Message.ShouldContain(AnthropicProviderNames.ThinkingBudgetTokensSetting);
    }

    [Fact]
    public void Adaptive_thinking_with_a_budget_is_an_error()
    {
        var exception = Should.Throw<TraconException>(() => Factory().CreateChatClient(TestData.Binding(providerSettings: Settings(
            (AnthropicProviderNames.ThinkingModeSetting, "adaptive"),
            (AnthropicProviderNames.ThinkingBudgetTokensSetting, 2048)))));

        exception.Message.ShouldContain(AnthropicProviderNames.ThinkingBudgetTokensSetting);
        exception.Message.ShouldContain("adaptive");
    }

    [Fact]
    public void A_setting_error_survives_the_registrys_failure_masking()
    {
        // The validation endpoint reports this text as invalid_setting; the
        // registry masks any other exception a provider throws (HATA-S4-005).
        var registry = new ModelProviderRegistry([new AnthropicModelProvider("anthropic", Factory(), [])]);

        var exception = Should.Throw<TraconException>(() => registry.CreateChatClient(
            TestData.Binding(providerSettings: Settings((AnthropicProviderNames.EffortSetting, "extreme")))));

        exception.Message.ShouldContain(AnthropicProviderNames.EffortSetting);
        exception.Message.ShouldNotContain("The model provider request failed.");
    }

    [Fact]
    public void The_client_reports_its_default_output_limit_and_a_high_effort()
    {
        using var high = Factory().CreateChatClient(
            TestData.Binding(providerSettings: Settings((AnthropicProviderNames.EffortSetting, "high"))));
        using var plain = Factory().CreateChatClient(TestData.Binding());

        var limit = high.GetService<ModelOutputLimit>().ShouldNotBeNull();
        limit.DefaultMaxOutputTokens.ShouldBe(4096);
        limit.HighReasoningEffort.ShouldBeTrue();

        plain.GetService<ModelOutputLimit>().ShouldNotBeNull().HighReasoningEffort.ShouldBeFalse();
    }

    [Fact]
    public async Task A_tenant_credential_client_sends_the_same_settings()
    {
        // BYOK (phase 65): the tenant's client is built by a second factory from
        // the credential. It must read the binding's settings the same way, or
        // a tenant on its own key silently loses effort and caching. A real
        // socket: the tenant client builds its own HttpClient.
        await using var server = StubHttpServer.Respond(200, "OK", CapturingAnthropicHandler.Response);

        var provider = new AnthropicModelProvider(
            AnthropicProviderNames.Anthropic,
            new AnthropicChatClientFactory(TestData.Options()),
            [new ModelDescriptor { Name = TestData.Model }],
            healthCheckOptions: TestData.Options(static options => options.MaxRetries = 0));

        using var client = provider.CreateChatClient(
            TestData.Binding(providerSettings: Settings(
                (AnthropicProviderNames.EffortSetting, "high"),
                (AnthropicProviderNames.PromptCachingSetting, true),
                (AnthropicProviderNames.PromptCachingTtlSetting, "1h"),
                (AnthropicProviderNames.ThinkingModeSetting, "adaptive"))),
            new ModelProviderCredential { ApiKey = "tenant-own-key-for-test", Endpoint = server.BaseAddress.ToString() });

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken);

        var head = await server.RequestHead;
        head.ShouldContain("x-api-key: tenant-own-key-for-test");

        var body = JsonNode.Parse(await server.RequestBody)!.AsObject();

        body["output_config"]!["effort"]!.GetValue<string>().ShouldBe("high");
        body["cache_control"]!["ttl"]!.GetValue<string>().ShouldBe("1h");
        body["thinking"]!["type"]!.GetValue<string>().ShouldBe("adaptive");
    }

    private static async Task<JsonObject> SendAsync(Dictionary<string, JsonElement> settings, ChatOptions? options = null)
    {
        var handler = new CapturingAnthropicHandler();
        using var client = Client(handler, settings);

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hi")], options, TestContext.Current.CancellationToken);

        return handler.Last;
    }

    private static IChatClient Client(CapturingAnthropicHandler handler, Dictionary<string, JsonElement> settings)
    {
        var sdkClient = new AnthropicClient(new global::Anthropic.Core.ClientOptions
        {
            ApiKey = TestData.ApiKey,
            HttpClient = new HttpClient(handler),
            MaxRetries = 0,
        });

        return AnthropicChatClientFactory.FromClient(sdkClient, TestData.Model)
            .CreateChatClient(TestData.Binding(providerSettings: settings));
    }

    private static AnthropicChatClientFactory Factory() => new(TestData.Options());

    private static Dictionary<string, JsonElement> Settings(params (string Key, object Value)[] entries)
        => TestData.Settings(entries);
}
