using Microsoft.Extensions.AI;
using Tracon.OpenAI.UnitTests.Infrastructure;

namespace Tracon.OpenAI.UnitTests;

/// <summary>
/// Verifies that provider options carry through correctly to the OpenAI client and the
/// produced pipeline, and that provider settings are rejected. No test makes a network call.
/// </summary>
public sealed class OpenAIChatClientFactoryTests
{
    [Fact]
    public void Endpoint_option_carries_through_to_the_produced_client()
    {
        // OpenAIClient.Endpoint is marked with OPENAI001, so verification goes through
        // the metadata MAF exposes instead.
        var endpoint = new Uri("https://intermediate-server.example.com/v1/");
        var factory = new OpenAIChatClientFactory(TestData.Options(o => o.Endpoint = endpoint));

        using var chatClient = factory.CreateChatClient(TestData.Binding(), OpenAIApiSurface.ChatCompletions);

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.ProviderUri.ShouldNotBeNull();
        metadata.ProviderUri!.Host.ShouldBe("intermediate-server.example.com");
    }

    [Fact]
    public void OpenAI_address_is_used_when_no_endpoint_is_given()
    {
        using var chatClient = CreateFactory().CreateChatClient(TestData.Binding(), OpenAIApiSurface.ChatCompletions);

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.ProviderUri.ShouldNotBeNull();
        metadata.ProviderUri!.Host.ShouldBe("api.openai.com");
    }

    [Fact]
    public void Organization_and_timeout_are_accepted()
    {
        // These two options are embedded inside OpenAIClientOptions and cannot be read
        // back from the outside. The test only verifies they are accepted and the
        // client can be built.
        var client = OpenAIChatClientFactory.CreateClient(TestData.Options(o =>
        {
            o.Organization = "org-test";
            o.Timeout = TimeSpan.FromSeconds(45);
        }));

        client.ShouldNotBeNull();
    }

    [Fact]
    public void Empty_api_key_produces_an_understandable_error()
    {
        var exception = Should.Throw<TraconException>(
            () => OpenAIChatClientFactory.CreateClient(new OpenAIProviderOptions { ApiKey = "   " }));

        exception.Message.ShouldContain(nameof(OpenAIProviderOptions.ApiKey));
        exception.Message.ShouldContain("UseOpenAI");
    }

    [Fact]
    public void Factory_returns_a_RAW_client_and_does_not_build_the_pipeline()
    {
        // 🚨 Phase 48: the tool call loop and telemetry moved into
        // ModelProviderRegistry. Had the factory built them, two nested
        // FunctionInvokingChatClient instances would form, and the content guard the
        // registry adds would sit OUTSIDE the loop — tool results would never be
        // inspected. The loop's EXISTENCE is verified at the registry level
        // (Tracon.Core.UnitTests, ModelProviderRegistryTests).
        using var chatClient = CreateFactory().CreateChatClient(TestData.Binding(), OpenAIApiSurface.ChatCompletions);

        chatClient.GetService(typeof(FunctionInvokingChatClient)).ShouldBeNull();
        chatClient.GetService(typeof(OpenTelemetryChatClient)).ShouldBeNull();
    }

    [Fact]
    public void Model_name_is_written_to_the_client_metadata()
    {
        using var chatClient = CreateFactory().CreateChatClient(
            TestData.Binding("gpt-4.1-mini"),
            OpenAIApiSurface.ChatCompletions);

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.DefaultModelId.ShouldBe("gpt-4.1-mini");
    }

    [Fact]
    public void Responses_surface_also_produces_a_client()
    {
        using var chatClient = CreateFactory().CreateChatClient(
            TestData.Binding("gpt-4.1-mini"),
            OpenAIApiSurface.Responses);

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.DefaultModelId.ShouldBe("gpt-4.1-mini");
    }

    [Fact]
    public void Default_model_is_used_when_the_model_name_is_empty()
    {
        var factory = new OpenAIChatClientFactory(TestData.Options(o => o.DefaultModel = "gpt-4o"));

        using var chatClient = factory.CreateChatClient(
            new ModelBinding { Provider = OpenAIProviderNames.ChatCompletions, Model = "   " },
            OpenAIApiSurface.ChatCompletions);

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.DefaultModelId.ShouldBe("gpt-4o");
    }

    [Fact]
    public void Missing_model_name_and_default_model_produces_an_understandable_error()
    {
        var factory = CreateFactory();

        var exception = Should.Throw<TraconException>(() => factory.CreateChatClient(
            new ModelBinding { Provider = OpenAIProviderNames.ChatCompletions, Model = "  " },
            OpenAIApiSurface.ChatCompletions));

        exception.Message.ShouldContain(nameof(OpenAIProviderOptions.DefaultModel));
    }

    // OpenAIApiSurface is internal, so a public theory cannot take it as a parameter.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Only_the_strict_schema_setting_is_supported(bool responsesSurface)
    {
        var exception = Should.Throw<TraconException>(() => CreateFactory().CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings(("openai.unknown", true))),
            Surface(responsesSurface)));

        exception.Message.ShouldContain("openai.unknown");
        exception.Message.ShouldContain("are not recognized");
        exception.Message.ShouldContain($"Supported keys: {OpenAIProviderNames.StrictJsonSchemaSetting}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_strict_schema_setting_is_accepted(bool responsesSurface)
    {
        using var client = CreateFactory().CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings((OpenAIProviderNames.StrictJsonSchemaSetting, true))),
            Surface(responsesSurface));

        client.ShouldNotBeNull();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void The_strict_schema_setting_wraps_the_client_only_when_it_is_on(bool strict, bool responsesSurface)
    {
        using var client = CreateFactory().CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings((OpenAIProviderNames.StrictJsonSchemaSetting, strict))),
            Surface(responsesSurface));

        (client is OpenAIStrictResponseSchemaChatClient).ShouldBe(strict);
    }

    [Fact]
    public void The_strict_schema_setting_is_read_as_text_from_a_flat_configuration_source()
    {
        // Environment variables and the command line carry every value as text.
        using var client = CreateFactory().CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings((OpenAIProviderNames.StrictJsonSchemaSetting, "true"))),
            Surface(responses: false));

        client.ShouldBeOfType<OpenAIStrictResponseSchemaChatClient>();
    }

    [Theory]
    [InlineData("maybe")]
    [InlineData("1")]
    public void A_strict_schema_value_that_is_not_a_boolean_is_rejected_without_repeating_the_value(string value)
    {
        var exception = Should.Throw<TraconException>(() => CreateFactory().CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings((OpenAIProviderNames.StrictJsonSchemaSetting, value))),
            Surface(responses: false)));

        exception.Message.ShouldContain(OpenAIProviderNames.StrictJsonSchemaSetting);
        exception.Message.ShouldContain("boolean");
        exception.Message.ShouldNotContain($"'{value}'");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Setting_belonging_to_another_provider_gives_a_separate_error_message(bool responsesSurface)
    {
        var exception = Should.Throw<TraconException>(() => CreateFactory().CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings(("anthropic.promptCaching", true))),
            Surface(responsesSurface)));

        // A wrong prefix means "you wrote the setting for another provider";
        // the fix for that differs from an unrecognized key.
        exception.Message.ShouldContain("do not belong");
        exception.Message.ShouldContain("anthropic.promptCaching");
    }

    [Fact]
    public void Null_binding_is_rejected()
        => Should.Throw<ArgumentNullException>(
            () => CreateFactory().CreateChatClient(null!, OpenAIApiSurface.ChatCompletions));

    [Fact]
    public void Null_client_is_rejected()
        => Should.Throw<ArgumentNullException>(() => OpenAIChatClientFactory.FromClient(client: null!));

    [Fact]
    public void Null_options_is_rejected()
        => Should.Throw<ArgumentNullException>(() => new OpenAIChatClientFactory(options: null!));

    private static OpenAIChatClientFactory CreateFactory() => new(TestData.Options());

    private static OpenAIApiSurface Surface(bool responses)
        => responses ? OpenAIApiSurface.Responses : OpenAIApiSurface.ChatCompletions;
}
