using AgentPrism.Google.UnitTests.Infrastructure;
using Microsoft.Extensions.AI;

namespace AgentPrism.Google.UnitTests;

/// <summary>
/// Fabrikanin model cozumu, boru hatti kurulumu ve saglayici ayarlarini dogrulamasi.
/// </summary>
public sealed class GoogleChatClientFactoryTests
{
    [Fact]
    public void Boru_hatti_tool_dongusu_ve_telemetri_icerir()
    {
        using var factory = Factory();
        using var chatClient = factory.CreateChatClient(TestData.Binding());

        chatClient.GetService(typeof(FunctionInvokingChatClient)).ShouldNotBeNull();
        chatClient.GetService(typeof(OpenTelemetryChatClient)).ShouldNotBeNull();
    }

    [Fact]
    public void Model_bos_ise_varsayilan_model_kullanilir()
    {
        using var factory = Factory(options => options.DefaultModel = TestData.Model);
        using var chatClient = factory.CreateChatClient(
            new ModelBinding { Provider = GoogleProviderNames.Google, Model = "  " });

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.DefaultModelId.ShouldBe(TestData.Model);
    }

    [Fact]
    public void Model_ve_varsayilan_model_yoksa_anlasilir_hata_verir()
    {
        using var factory = Factory();

        var exception = Should.Throw<AgentPrismException>(() => factory.CreateChatClient(
            new ModelBinding { Provider = GoogleProviderNames.Google, Model = " " }));

        exception.Message.ShouldContain(nameof(ModelBinding.Model));
        exception.Message.ShouldContain(nameof(GoogleProviderOptions.DefaultModel));
    }

    [Fact]
    public void Anahtarsiz_istemci_kurulumu_hata_verir()
        => Should.Throw<AgentPrismException>(() => GoogleChatClientFactory.CreateClient(new GoogleProviderOptions()))
            .Message.ShouldContain(nameof(GoogleProviderOptions.ApiKey));

    [Fact]
    public void Desteklenen_saglayici_ayarlari_kabul_edilir()
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
    public void Taninmayan_saglayici_ayari_hata_verir_ve_gecerli_anahtarlari_listeler()
    {
        using var factory = Factory();

        var exception = Should.Throw<AgentPrismException>(() => factory.CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings(("google.safety.bilinmeyen", "BLOCK_NONE")))));

        exception.Message.ShouldContain("google.safety.bilinmeyen");
        exception.Message.ShouldContain(GoogleProviderNames.SafetyHarassmentSetting);
    }

    [Fact]
    public void Baska_saglayiciya_ait_ayar_hata_verir()
    {
        using var factory = Factory();

        var exception = Should.Throw<AgentPrismException>(() => factory.CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings(("anthropic.promptCaching", true)))));

        exception.Message.ShouldContain("anthropic.promptCaching");
        exception.Message.ShouldContain(GoogleProviderNames.SettingsPrefix);
    }

    [Fact]
    public void Taninmayan_guvenlik_esigi_hata_verir_ve_gecerli_degerleri_listeler()
    {
        using var factory = Factory();

        var exception = Should.Throw<AgentPrismException>(() => factory.CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings(
                (GoogleProviderNames.SafetyHarassmentSetting, "HERSEYI_ENGELLE")))));

        exception.Message.ShouldContain("HERSEYI_ENGELLE");
        exception.Message.ShouldContain("BLOCK_ONLY_HIGH");
        exception.Message.ShouldContain("BLOCK_NONE");
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(65536)]
    public void Aralik_disi_dusunme_butcesi_reddedilir(int budget)
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
    public void Sinir_degerleri_kabul_edilir(int budget)
    {
        using var factory = Factory();
        using var chatClient = factory.CreateChatClient(TestData.Binding(
            providerSettings: TestData.Settings((GoogleProviderNames.ThinkingBudgetTokensSetting, budget))));

        chatClient.ShouldNotBeNull();
    }

    [Fact]
    public void Yanlis_tipli_ayar_degeri_reddedilir()
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
