using Tracon.OpenAI.UnitTests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon.OpenAI.UnitTests;

/// <summary>Verifies the provider's catalog and client production behavior.</summary>
public sealed class OpenAIModelProviderTests
{
    [Fact]
    public void Provider_name_and_surface_are_preserved()
    {
        var provider = CreateProvider(OpenAIProviderNames.Responses, OpenAIApiSurface.Responses);

        provider.Name.ShouldBe(OpenAIProviderNames.Responses);
        provider.ApiSurface.ShouldBe(OpenAIApiSurface.Responses);
    }

    [Fact]
    public void Client_is_produced_for_a_model_in_the_catalog()
    {
        using var chatClient = CreateProvider().CreateChatClient(TestData.Binding("gpt-4o-mini"));

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();
        metadata.DefaultModelId.ShouldBe("gpt-4o-mini");
    }

    [Fact]
    public void Model_absent_from_the_catalog_is_not_rejected()
    {
        // The catalog is not a validation list. Otherwise every new model OpenAI
        // publishes would be unusable until Tracon released a new version.
        using var chatClient = CreateProvider().CreateChatClient(TestData.Binding("not-yet-released-model"));

        chatClient.ShouldNotBeNull();
    }

    [Fact]
    public void Model_absent_from_the_catalog_is_logged()
    {
        using var loggerProvider = new RecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(loggerProvider));

        var provider = CreateProvider(logger: loggerFactory.CreateLogger<OpenAIModelProvider>());

        using var chatClient = provider.CreateChatClient(TestData.Binding("not-yet-released-model"));

        loggerProvider.AllText.ShouldContain("not-yet-released-model");
    }

    [Fact]
    public void Empty_name_is_rejected()
        => Should.Throw<ArgumentException>(() => new OpenAIModelProvider(
            "  ",
            OpenAIApiSurface.ChatCompletions,
            new OpenAIChatClientFactory(TestData.Options()),
            TestCatalog));

    [Fact]
    public void Null_factory_is_rejected()
        => Should.Throw<ArgumentNullException>(() => new OpenAIModelProvider(
            OpenAIProviderNames.ChatCompletions,
            OpenAIApiSurface.ChatCompletions,
            chatClientFactory: null!,
            TestCatalog));

    [Fact]
    public void Null_binding_is_rejected()
        => Should.Throw<ArgumentNullException>(() => CreateProvider().CreateChatClient(null!));

    private static IReadOnlyList<ModelDescriptor> TestCatalog { get; } =
    [
        new ModelDescriptor { Name = "gpt-4o-mini" },
    ];

    private static OpenAIModelProvider CreateProvider(
        string name = OpenAIProviderNames.ChatCompletions,
        OpenAIApiSurface apiSurface = OpenAIApiSurface.ChatCompletions,
        ILogger<OpenAIModelProvider>? logger = null)
        => new(
            name,
            apiSurface,
            new OpenAIChatClientFactory(TestData.Options()),
            TestCatalog,
            logger);
}
