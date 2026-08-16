using AgentPrism.Google.UnitTests.Infrastructure;
using Microsoft.Extensions.AI;

namespace AgentPrism.Google.UnitTests;

/// <summary>
/// The factory's model resolution, pipeline setup, and provider settings validation.
/// </summary>
public sealed class GoogleChatClientFactoryTests
{
    [Fact]
    public void Factory_returns_RAW_client_and_does_not_build_the_pipeline()
    {
        // 🚨 Phase 48: the tool call loop and telemetry moved to
        // ModelProviderRegistry. If the factory built them, two nested
        // FunctionInvokingChatClient instances would form, and the content guard
        // added by the ledger would sit OUTSIDE the loop — tool results would
        // never be inspected.
        using var factory = Factory();
        using var chatClient = factory.CreateChatClient(TestData.Binding());

        chatClient.GetService(typeof(FunctionInvokingChatClient)).ShouldBeNull();
        chatClient.GetService(typeof(OpenTelemetryChatClient)).ShouldBeNull();
    }

    [Fact]
    public void Empty_model_uses_the_default_model()
    {
        using var factory = Factory(options => options.DefaultModel = TestData.Model);
        using var chatClient = factory.CreateChatClient(
            new ModelBinding { Provider = GoogleProviderNames.Google, Model = "  " });

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.DefaultModelId.ShouldBe(TestData.Model);
    }

    [Fact]
    public void Missing_model_and_default_model_gives_a_clear_error()
    {
        using var factory = Factory();

        var exception = Should.Throw<AgentPrismException>(() => factory.CreateChatClient(
            new ModelBinding { Provider = GoogleProviderNames.Google, Model = " " }));

        exception.Message.ShouldContain(nameof(ModelBinding.Model));
        exception.Message.ShouldContain(nameof(GoogleProviderOptions.DefaultModel));
    }

    [Fact]
    public void Client_setup_without_a_key_fails()
        => Should.Throw<AgentPrismException>(() => GoogleChatClientFactory.CreateClient(new GoogleProviderOptions()))
            .Message.ShouldContain(nameof(GoogleProviderOptions.ApiKey));

    [Fact]
    public void Supported_provider_settings_are_accepted()
    {
        using var factory = Factory();
        using var chatClient = factory.CreateChatClient(TestData.Binding(
            providerSettings: TestData.Settings(
                (GoogleProviderNames.SafetyHarassmentSetting, "BLOCK_ONLY_HIGH"),
                (GoogleProviderNames.SafetyDangerousContentSetting, "block_none"),
                (GoogleProviderNames.ThinkingBudgetTokensSetting, 512),
                (GoogleProviderNames.ThinkingIncludeThoughtsSetting, true))));

        chatClient.ShouldNotBeNull();
    }

    [Fact]
    public void Unknown_provider_setting_fails_and_lists_valid_keys()
    {
        using var factory = Factory();

        var exception = Should.Throw<AgentPrismException>(() => factory.CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings(("google.safety.unknown", "BLOCK_NONE")))));

        exception.Message.ShouldContain("google.safety.unknown");
        exception.Message.ShouldContain(GoogleProviderNames.SafetyHarassmentSetting);
    }

    [Fact]
    public void Setting_belonging_to_another_provider_fails()
    {
        using var factory = Factory();

        var exception = Should.Throw<AgentPrismException>(() => factory.CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings(("anthropic.promptCaching", true)))));

        exception.Message.ShouldContain("anthropic.promptCaching");
        exception.Message.ShouldContain(GoogleProviderNames.SettingsPrefix);
    }

    [Fact]
    public void Unknown_safety_threshold_fails_and_lists_valid_values()
    {
        using var factory = Factory();

        var exception = Should.Throw<AgentPrismException>(() => factory.CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings(
                (GoogleProviderNames.SafetyHarassmentSetting, "BLOCK_EVERYTHING")))));

        exception.Message.ShouldContain("BLOCK_EVERYTHING");
        exception.Message.ShouldContain("BLOCK_ONLY_HIGH");
        exception.Message.ShouldContain("BLOCK_NONE");
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(65536)]
    public void Out_of_range_thinking_budget_is_rejected(int budget)
    {
        using var factory = Factory();

        var exception = Should.Throw<AgentPrismException>(() => factory.CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings(
                (GoogleProviderNames.ThinkingBudgetTokensSetting, budget)))));

        exception.Message.ShouldContain(GoogleProviderNames.ThinkingBudgetTokensSetting);
        exception.Message.ShouldContain("65535");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(65535)]
    public void Boundary_values_are_accepted(int budget)
    {
        using var factory = Factory();
        using var chatClient = factory.CreateChatClient(TestData.Binding(
            providerSettings: TestData.Settings((GoogleProviderNames.ThinkingBudgetTokensSetting, budget))));

        chatClient.ShouldNotBeNull();
    }

    [Fact]
    public void Wrong_typed_setting_value_is_rejected()
    {
        using var factory = Factory();

        Should.Throw<AgentPrismException>(() => factory.CreateChatClient(
                TestData.Binding(providerSettings: TestData.Settings(
                    (GoogleProviderNames.ThinkingIncludeThoughtsSetting, 3)))))
            .Message.ShouldContain(GoogleProviderNames.ThinkingIncludeThoughtsSetting);
    }

    private static GoogleChatClientFactory Factory(Action<GoogleProviderOptions>? configure = null)
        => new(TestData.Options(configure));
}
