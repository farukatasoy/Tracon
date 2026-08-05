using AgentPrism.Anthropic.UnitTests.Infrastructure;
using Microsoft.Extensions.AI;

namespace AgentPrism.Anthropic.UnitTests;

/// <summary>
/// Fabrikanin model cozumu, boru hatti kurulumu ve saglayici ayarlarini dogrulamasi.
/// </summary>
public sealed class AnthropicChatClientFactoryTests
{
    [Fact]
    public void Boru_hatti_tool_dongusu_ve_telemetri_icerir()
    {
        using var chatClient = Factory().CreateChatClient(TestData.Binding());

        // Tool dongusu MAF'a birakilir; span'ler Faz 6'nin kaynagi altinda uretilir.
        chatClient.GetService(typeof(FunctionInvokingChatClient)).ShouldNotBeNull();
        chatClient.GetService(typeof(OpenTelemetryChatClient)).ShouldNotBeNull();
    }

    [Fact]
    public void Model_bos_ise_varsayilan_model_kullanilir()
    {
        using var chatClient = Factory(options => options.DefaultModel = TestData.Model)
            .CreateChatClient(new ModelBinding { Provider = AnthropicProviderNames.Anthropic, Model = "  " });

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.DefaultModelId.ShouldBe(TestData.Model);
    }

    [Fact]
    public void Model_ve_varsayilan_model_yoksa_anlasilir_hata_verir()
    {
        var exception = Should.Throw<AgentPrismException>(() => Factory()
            .CreateChatClient(new ModelBinding { Provider = AnthropicProviderNames.Anthropic, Model = " " }));

        exception.Message.ShouldContain(nameof(ModelBinding.Model));
        exception.Message.ShouldContain(nameof(AnthropicProviderOptions.DefaultModel));
    }

    [Fact]
    public void Anahtarsiz_istemci_kurulumu_hata_verir()
    {
        var exception = Should.Throw<AgentPrismException>(
            () => AnthropicChatClientFactory.CreateClient(new AnthropicProviderOptions()));

        exception.Message.ShouldContain(nameof(AnthropicProviderOptions.ApiKey));
    }

    [Fact]
    public void Endpoint_verilirse_istemci_ustverisine_yansir()
    {
        using var chatClient = Factory(options => options.Endpoint = new Uri("https://ornek.gecit/v1"))
            .CreateChatClient(TestData.Binding());

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.ProviderUri!.ToString().ShouldStartWith("https://ornek.gecit/");
    }

    [Fact]
    public void Desteklenen_saglayici_ayarlari_kabul_edilir()
    {
        using var chatClient = Factory().CreateChatClient(TestData.Binding(
            providerSettings: TestData.Settings(
                (AnthropicProviderNames.PromptCachingSetting, true),
                (AnthropicProviderNames.ThinkingBudgetTokensSetting, 2048))));

        chatClient.ShouldNotBeNull();
    }

    [Fact]
    public void Taninmayan_saglayici_ayari_hata_verir_ve_gecerli_anahtarlari_listeler()
    {
        var exception = Should.Throw<AgentPrismException>(() => Factory().CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings(("anthropic.bilinmeyenAyar", true)))));

        // Sessizce yok sayilmaz (K-034 deseni); mesaj gecerli anahtarlari yazar.
        exception.Message.ShouldContain("anthropic.bilinmeyenAyar");
        exception.Message.ShouldContain(AnthropicProviderNames.PromptCachingSetting);
        exception.Message.ShouldContain(AnthropicProviderNames.ThinkingBudgetTokensSetting);
    }

    [Fact]
    public void Baska_saglayiciya_ait_ayar_hata_verir()
    {
        var exception = Should.Throw<AgentPrismException>(() => Factory().CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings(("google.safety.harassment", "BLOCK_NONE")))));

        exception.Message.ShouldContain("google.safety.harassment");
        exception.Message.ShouldContain(AnthropicProviderNames.SettingsPrefix);
    }

    [Fact]
    public void Sifir_veya_negatif_dusunme_butcesi_reddedilir()
    {
        var exception = Should.Throw<AgentPrismException>(() => Factory().CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings(
                (AnthropicProviderNames.ThinkingBudgetTokensSetting, 0)))));

        exception.Message.ShouldContain(AnthropicProviderNames.ThinkingBudgetTokensSetting);
    }

    [Fact]
    public void Yanlis_tipli_ayar_degeri_reddedilir()
    {
        var exception = Should.Throw<AgentPrismException>(() => Factory().CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings(
                (AnthropicProviderNames.ThinkingBudgetTokensSetting, "cok")))));

        exception.Message.ShouldContain(AnthropicProviderNames.ThinkingBudgetTokensSetting);
    }

    [Fact]
    public void Sifir_varsayilan_cikti_siniri_reddedilir()
    {
        // Anthropic Messages API'si max_tokens alanini zorunlu tutar; sifir bir istegi
        // calisma aninda kirardi.
        Should.Throw<ArgumentOutOfRangeException>(() => new AnthropicChatClientFactory(
            AnthropicChatClientFactory.CreateClient(TestData.Options()),
            TestData.Model,
            defaultMaxOutputTokens: 0));
    }

    private static AnthropicChatClientFactory Factory(Action<AnthropicProviderOptions>? configure = null)
        => new(TestData.Options(configure));
}
