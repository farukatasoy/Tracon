using AgentPrism.OpenAI.UnitTests.Infrastructure;

namespace AgentPrism.OpenAI.UnitTests;

/// <summary>
/// <see cref="OpenAIModelProvider.GetConfigurationDiagnostic"/>'in K-059'a uygun
/// davrandigini dogrular: anahtarin DEGERI degil, cozulup cozulmedigi bilgisi doner.
/// </summary>
public sealed class OpenAIModelProviderConfigurationDiagnosticsTests
{
    [Fact]
    public void Anahtar_verilmisse_cozuldu_ve_ipucu_yoktur()
    {
        var provider = CreateProvider(healthCheckOptions: TestData.Options());

        var diagnostic = provider.GetConfigurationDiagnostic();

        diagnostic.ShouldNotBeNull();
        diagnostic.Key.ShouldBe("AgentPrism:Providers:OpenAI:ApiKey");
        diagnostic.Resolved.ShouldBeTrue();
        diagnostic.Hint.ShouldBeNull();
    }

    [Fact]
    public void Anahtar_bossa_cozulmedi_ve_ipucu_verilir_ama_deger_verilmez()
    {
        // ChatClientFactory GECERLI bir anahtarla kurulur (kurucusu bos anahtari
        // reddeder) — bu test yalniz TESHIS icin ayrica gecirilen healthCheckOptions'in
        // BOS anahtarini dogrular; ikisi kasitli olarak farkli nesnelerdir.
        var provider = CreateProvider(healthCheckOptions: TestData.Options(static o => o.ApiKey = null));

        var diagnostic = provider.GetConfigurationDiagnostic();

        diagnostic.ShouldNotBeNull();
        diagnostic.Resolved.ShouldBeFalse();
        diagnostic.Hint.ShouldNotBeNullOrEmpty();
        diagnostic.Hint.ShouldNotContain(TestData.ApiKey);
    }

    [Fact]
    public void Saglik_denetimi_ayarlari_verilmemisse_teshis_null_doner()
    {
        var provider = new OpenAIModelProvider(
            OpenAIProviderNames.ChatCompletions,
            OpenAIApiSurface.ChatCompletions,
            new OpenAIChatClientFactory(TestData.Options()),
            [new ModelDescriptor { Name = "gpt-4o-mini" }]);

        provider.GetConfigurationDiagnostic().ShouldBeNull();
    }

    [Fact]
    public void UseOpenAICompatible_sabit_bolum_olmadigi_icin_teshis_bildirmez()
    {
        // configurationSectionKey: null -> UseOpenAICompatible()'in kod-tanimli,
        // sabit olmayan anahtarini yanlis raporlamak yerine hic raporlamaz.
        var provider = new OpenAIModelProvider(
            "openrouter",
            OpenAIApiSurface.ChatCompletions,
            new OpenAIChatClientFactory(TestData.Options()),
            [new ModelDescriptor { Name = "gpt-4o-mini" }],
            healthCheckOptions: TestData.Options(),
            configurationSectionKey: null);

        provider.GetConfigurationDiagnostic().ShouldBeNull();
    }

    private static OpenAIModelProvider CreateProvider(OpenAIProviderOptions healthCheckOptions)
        => new(
            OpenAIProviderNames.ChatCompletions,
            OpenAIApiSurface.ChatCompletions,
            new OpenAIChatClientFactory(TestData.Options()),
            [new ModelDescriptor { Name = "gpt-4o-mini" }],
            healthCheckOptions: healthCheckOptions);
}
