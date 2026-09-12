using Microsoft.Extensions.AI;
using Tracon.OpenAI.UnitTests.Infrastructure;

namespace Tracon.OpenAI.UnitTests;

/// <summary>
/// Verifies that provider options carry through correctly to the OpenAI client and the
/// produced pipeline. No test makes a network call.
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
}
