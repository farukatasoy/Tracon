using Microsoft.Extensions.AI;
using Tracon.Anthropic.UnitTests.Infrastructure;

namespace Tracon.Anthropic.UnitTests;

/// <summary>
/// The factory's model resolution, pipeline setup, and provider settings validation.
/// </summary>
public sealed class AnthropicChatClientFactoryTests
{
    [Fact]
    public void Factory_returns_a_RAW_client_and_does_not_build_the_pipeline()
    {
        // 🚨 Phase 48: the tool-call loop and telemetry moved to
        // ModelProviderRegistry. If the factory built them, two nested
        // FunctionInvokingChatClient instances would form and the content guard
        // the registry adds would stay OUTSIDE the loop — tool results would
        // never be inspected.
        using var chatClient = Factory().CreateChatClient(TestData.Binding());

        chatClient.GetService(typeof(FunctionInvokingChatClient)).ShouldBeNull();
        chatClient.GetService(typeof(OpenTelemetryChatClient)).ShouldBeNull();
    }

    [Fact]
    public void Empty_model_falls_back_to_the_default_model()
    {
        using var chatClient = Factory(options => options.DefaultModel = TestData.Model)
            .CreateChatClient(new ModelBinding { Provider = AnthropicProviderNames.Anthropic, Model = "  " });

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.DefaultModelId.ShouldBe(TestData.Model);
    }

    [Fact]
    public void Missing_model_and_default_model_gives_a_clear_error()
    {
        var exception = Should.Throw<TraconException>(() => Factory()
            .CreateChatClient(new ModelBinding { Provider = AnthropicProviderNames.Anthropic, Model = " " }));

        exception.Message.ShouldContain(nameof(ModelBinding.Model));
        exception.Message.ShouldContain(nameof(AnthropicProviderOptions.DefaultModel));
    }

    [Fact]
    public void Client_setup_without_a_key_fails()
    {
        var exception = Should.Throw<TraconException>(
            () => AnthropicChatClientFactory.CreateClient(new AnthropicProviderOptions()));

        exception.Message.ShouldContain(nameof(AnthropicProviderOptions.ApiKey));
    }

    [Fact]
    public void Given_endpoint_is_reflected_in_client_metadata()
    {
        using var chatClient = Factory(options => options.Endpoint = new Uri("https://example.gateway/v1"))
            .CreateChatClient(TestData.Binding());

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.ProviderUri!.ToString().ShouldStartWith("https://example.gateway/");
    }

    [Fact]
    public void Supported_provider_settings_are_accepted()
    {
        using var chatClient = Factory().CreateChatClient(TestData.Binding(
            providerSettings: TestData.Settings(
                (AnthropicProviderNames.PromptCachingSetting, true),
                (AnthropicProviderNames.ThinkingBudgetTokensSetting, 2048))));

        chatClient.ShouldNotBeNull();
    }

    [Fact]
    public void Unrecognized_provider_setting_fails_and_lists_the_valid_keys()
    {
        var exception = Should.Throw<TraconException>(() => Factory().CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings(("anthropic.unknownSetting", true)))));

        // Not silently ignored (K-034 pattern); the message lists the valid keys.
        exception.Message.ShouldContain("anthropic.unknownSetting");
        exception.Message.ShouldContain(AnthropicProviderNames.PromptCachingSetting);
        exception.Message.ShouldContain(AnthropicProviderNames.ThinkingBudgetTokensSetting);
    }

    [Fact]
    public void Setting_belonging_to_another_provider_fails()
    {
        var exception = Should.Throw<TraconException>(() => Factory().CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings(("google.safety.harassment", "BLOCK_NONE")))));

        exception.Message.ShouldContain("google.safety.harassment");
        exception.Message.ShouldContain(AnthropicProviderNames.SettingsPrefix);
    }

    [Fact]
    public void Zero_or_negative_thinking_budget_is_rejected()
    {
        var exception = Should.Throw<TraconException>(() => Factory().CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings(
                (AnthropicProviderNames.ThinkingBudgetTokensSetting, 0)))));

        exception.Message.ShouldContain(AnthropicProviderNames.ThinkingBudgetTokensSetting);
    }

    [Fact]
    public void Wrongly_typed_setting_value_is_rejected()
    {
        var exception = Should.Throw<TraconException>(() => Factory().CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings(
                (AnthropicProviderNames.ThinkingBudgetTokensSetting, "too much")))));

        exception.Message.ShouldContain(AnthropicProviderNames.ThinkingBudgetTokensSetting);
    }

    [Fact]
    public void Zero_default_output_limit_is_rejected()
    {
        // The Anthropic Messages API requires the max_tokens field; zero would
        // break a request at run time.
        Should.Throw<ArgumentOutOfRangeException>(() => AnthropicChatClientFactory.FromClient(
            AnthropicChatClientFactory.CreateClient(TestData.Options()),
            TestData.Model,
            defaultMaxOutputTokens: 0));
    }

    private static AnthropicChatClientFactory Factory(Action<AnthropicProviderOptions>? configure = null)
        => new(TestData.Options(configure));
}
