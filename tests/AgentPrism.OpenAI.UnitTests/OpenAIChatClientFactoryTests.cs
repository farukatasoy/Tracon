using AgentPrism.OpenAI.UnitTests.Infrastructure;
using Microsoft.Extensions.AI;

namespace AgentPrism.OpenAI.UnitTests;

/// <summary>
/// Saglayici ayarlarinin OpenAI istemcisine ve uretilen boru hattina dogru
/// aktarildigini dogrular. Hicbir test ag cagrisi yapmaz.
/// </summary>
public sealed class OpenAIChatClientFactoryTests
{
    [Fact]
    public void Endpoint_ayari_uretilen_istemciye_aktarilir()
    {
        // OpenAIClient.Endpoint ozelligi OPENAI001 ile isaretli oldugu icin
        // dogrulama, MAF'in disariya verdigi ustveri uzerinden yapilir.
        var endpoint = new Uri("https://ara-sunucu.example.com/v1/");
        var factory = new OpenAIChatClientFactory(TestData.Options(o => o.Endpoint = endpoint));

        using var chatClient = factory.CreateChatClient(TestData.Binding(), OpenAIApiSurface.ChatCompletions);

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.ProviderUri.ShouldNotBeNull();
        metadata.ProviderUri!.Host.ShouldBe("ara-sunucu.example.com");
    }

    [Fact]
    public void Endpoint_verilmezse_OpenAI_adresi_kullanilir()
    {
        using var chatClient = CreateFactory().CreateChatClient(TestData.Binding(), OpenAIApiSurface.ChatCompletions);

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.ProviderUri.ShouldNotBeNull();
        metadata.ProviderUri!.Host.ShouldBe("api.openai.com");
    }

    [Fact]
    public void Kurulus_ve_sure_siniri_kabul_edilir()
    {
        // Bu iki ayar OpenAIClientOptions icine gomulur ve disaridan okunamaz.
        // Test, en azindan kabul edildiklerini ve istemcinin kurulabildigini dogrular.
        var client = OpenAIChatClientFactory.CreateClient(TestData.Options(o =>
        {
            o.Organization = "org-test";
            o.Timeout = TimeSpan.FromSeconds(45);
        }));

        client.ShouldNotBeNull();
    }

    [Fact]
    public void Api_anahtari_bos_ise_anlasilir_hata_verilir()
    {
        var exception = Should.Throw<AgentPrismException>(
            () => OpenAIChatClientFactory.CreateClient(new OpenAIProviderOptions { ApiKey = "   " }));

        exception.Message.ShouldContain(nameof(OpenAIProviderOptions.ApiKey));
        exception.Message.ShouldContain("UseOpenAI");
    }

    [Fact]
    public void Fabrika_HAM_istemci_doner_boru_hattini_kurmaz()
    {
        // 🚨 Faz 48: tool cagri dongusu ve telemetri ModelProviderRegistry'ye
        // tasindi. Fabrika onlari kursaydi ic ice iki FunctionInvokingChatClient
        // olusur ve defterin ekledigi icerik guard'i dongunun DISINDA kalirdi —
        // tool sonuclari hic denetlenmezdi. Dongunun VARLIGI defter duzeyinde
        // dogrulanir (AgentPrism.Core.UnitTests, ModelProviderRegistryTests).
        using var chatClient = CreateFactory().CreateChatClient(TestData.Binding(), OpenAIApiSurface.ChatCompletions);

        chatClient.GetService(typeof(FunctionInvokingChatClient)).ShouldBeNull();
        chatClient.GetService(typeof(OpenTelemetryChatClient)).ShouldBeNull();
    }

    [Fact]
    public void Model_adi_istemci_ustverisine_yazilir()
    {
        using var chatClient = CreateFactory().CreateChatClient(
            TestData.Binding("gpt-4.1-mini"),
            OpenAIApiSurface.ChatCompletions);

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.DefaultModelId.ShouldBe("gpt-4.1-mini");
    }

    [Fact]
    public void Responses_yuzeyi_de_istemci_uretir()
    {
        using var chatClient = CreateFactory().CreateChatClient(
            TestData.Binding("gpt-4.1-mini"),
            OpenAIApiSurface.Responses);

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.DefaultModelId.ShouldBe("gpt-4.1-mini");
    }

    [Fact]
    public void Model_adi_bos_ise_varsayilan_model_kullanilir()
    {
        var factory = new OpenAIChatClientFactory(TestData.Options(o => o.DefaultModel = "gpt-4o"));

        using var chatClient = factory.CreateChatClient(
            new ModelBinding { Provider = OpenAIProviderNames.ChatCompletions, Model = "   " },
            OpenAIApiSurface.ChatCompletions);

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.DefaultModelId.ShouldBe("gpt-4o");
    }

    [Fact]
    public void Model_adi_ve_varsayilan_model_yoksa_anlasilir_hata_verilir()
    {
        var factory = CreateFactory();

        var exception = Should.Throw<AgentPrismException>(() => factory.CreateChatClient(
            new ModelBinding { Provider = OpenAIProviderNames.ChatCompletions, Model = "  " },
            OpenAIApiSurface.ChatCompletions));

        exception.Message.ShouldContain(nameof(OpenAIProviderOptions.DefaultModel));
    }

    [Fact]
    public void Null_baglanti_reddedilir()
        => Should.Throw<ArgumentNullException>(
            () => CreateFactory().CreateChatClient(null!, OpenAIApiSurface.ChatCompletions));

    [Fact]
    public void Null_istemci_reddedilir()
        => Should.Throw<ArgumentNullException>(() => new OpenAIChatClientFactory(client: null!));

    [Fact]
    public void Null_ayar_reddedilir()
        => Should.Throw<ArgumentNullException>(() => new OpenAIChatClientFactory(options: null!));

    private static OpenAIChatClientFactory CreateFactory() => new(TestData.Options());
}
