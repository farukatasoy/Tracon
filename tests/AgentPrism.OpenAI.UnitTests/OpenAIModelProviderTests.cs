using AgentPrism.OpenAI.UnitTests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism.OpenAI.UnitTests;

/// <summary>Saglayicinin katalog ve istemci uretimi davranisini dogrular.</summary>
public sealed class OpenAIModelProviderTests
{
    [Fact]
    public void Saglayici_adi_ve_yuzeyi_korunur()
    {
        var provider = CreateProvider(OpenAIProviderNames.Responses, OpenAIApiSurface.Responses);

        provider.Name.ShouldBe(OpenAIProviderNames.Responses);
        provider.ApiSurface.ShouldBe(OpenAIApiSurface.Responses);
    }

    [Fact]
    public void Katalogdaki_model_icin_istemci_uretilir()
    {
        using var chatClient = CreateProvider().CreateChatClient(TestData.Binding("gpt-4o-mini"));

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();
        metadata.DefaultModelId.ShouldBe("gpt-4o-mini");
    }

    [Fact]
    public void Katalogda_olmayan_model_reddedilmez()
    {
        // Katalog bir dogrulama listesi degildir. Aksi halde OpenAI'in yayinladigi
        // her yeni model, AgentPrism'in yeni bir surumu cikana kadar kullanilamazdi.
        using var chatClient = CreateProvider().CreateChatClient(TestData.Binding("henuz-yayinlanmamis-model"));

        chatClient.ShouldNotBeNull();
    }

    [Fact]
    public void Katalogda_olmayan_model_gunluge_yazilir()
    {
        using var loggerProvider = new RecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(loggerProvider));

        var provider = CreateProvider(logger: loggerFactory.CreateLogger<OpenAIModelProvider>());

        using var chatClient = provider.CreateChatClient(TestData.Binding("henuz-yayinlanmamis-model"));

        loggerProvider.AllText.ShouldContain("henuz-yayinlanmamis-model");
    }

    [Fact]
    public void Bos_ad_reddedilir()
        => Should.Throw<ArgumentException>(() => new OpenAIModelProvider(
            "  ",
            OpenAIApiSurface.ChatCompletions,
            new OpenAIChatClientFactory(TestData.Options()),
            TestCatalog));

    [Fact]
    public void Null_fabrika_reddedilir()
        => Should.Throw<ArgumentNullException>(() => new OpenAIModelProvider(
            OpenAIProviderNames.ChatCompletions,
            OpenAIApiSurface.ChatCompletions,
            chatClientFactory: null!,
            TestCatalog));

    [Fact]
    public void Null_baglanti_reddedilir()
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
