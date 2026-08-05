using System.Net;
using System.Text;
using AgentPrism.Google.UnitTests.Infrastructure;

namespace AgentPrism.Google.UnitTests;

/// <summary>
/// Saglik denetiminin adres birlestirmesi ve yanit ayristirmasi. Ag cagrisi yapilmaz.
/// </summary>
public sealed class GoogleProviderHealthCheckTests
{
    [Fact]
    public void Adres_verilmezse_resmi_uc_ve_varsayilan_surum_kullanilir()
        => GoogleProviderHealthCheck.BuildModelsEndpoint(null, null)
            .ToString().ShouldBe("https://generativelanguage.googleapis.com/v1beta/models");

    [Fact]
    public void Api_surumu_verilirse_adrese_girer()
        => GoogleProviderHealthCheck.BuildModelsEndpoint(null, "v1")
            .ToString().ShouldBe("https://generativelanguage.googleapis.com/v1/models");

    [Fact]
    public void Egik_cizgisiz_taban_adres_son_parcayi_yutmaz()
        => GoogleProviderHealthCheck.BuildModelsEndpoint(new Uri("https://ornek.gecit/genai"), "v1beta")
            .ToString().ShouldBe("https://ornek.gecit/genai/v1beta/models");

    [Fact]
    public async Task Yanittan_model_adlari_okunur_ve_onek_temizlenir()
    {
        using var response = Json("""
            {"models":[{"name":"models/gemini-3.6-flash"},{"name":"models/gemini-3.1-pro-preview"}]}
            """);

        var models = await GoogleProviderHealthCheck.ReadModelIdsAsync(response, TestContext.Current.CancellationToken);

        // ModelBinding.Model alani "models/" onegini tasimaz; onek temizlenmeli.
        models.ShouldBe(["gemini-3.1-pro-preview", "gemini-3.6-flash"]);
    }

    [Fact]
    public async Task Oneksiz_ad_oldugu_gibi_kalir()
    {
        using var response = Json("""{"models":[{"name":"ozel-model"}]}""");

        var models = await GoogleProviderHealthCheck.ReadModelIdsAsync(response, TestContext.Current.CancellationToken);

        models.ShouldBe(["ozel-model"]);
    }

    [Fact]
    public async Task Beklenmeyen_govde_bos_liste_dondurur()
    {
        // OpenAI bicimi ("data") Gemini icin gecerli degildir; sessizce bos donmeli.
        using var response = Json("""{"data":[{"id":"gemini-3.6-flash"}]}""");

        var models = await GoogleProviderHealthCheck.ReadModelIdsAsync(response, TestContext.Current.CancellationToken);

        models.ShouldBeEmpty();
    }

    [Fact]
    public async Task Baglanamayan_ucun_detayinda_ne_anahtar_ne_adres_gorunur()
    {
        var options = TestData.Options(o =>
        {
            o.Endpoint = new Uri("http://127.0.0.1:1");
            o.Timeout = TimeSpan.FromSeconds(5);
        });

        var health = await new GoogleProviderHealthCheck(GoogleProviderNames.Google, options)
            .CheckHealthAsync(TestContext.Current.CancellationToken);

        health.Status.ShouldBe(ModelProviderHealthStatus.Unhealthy);
        health.Detail.ShouldNotBeNull();
        health.Detail.ShouldNotContain(TestData.ApiKey);
        health.Detail.ShouldNotContain("127.0.0.1");
    }

    [Fact]
    public async Task Saglik_ayari_verilmeyen_saglayici_bilinmiyor_dondurur()
    {
        using var factory = new GoogleChatClientFactory(TestData.Options());
        var provider = new GoogleModelProvider(GoogleProviderNames.Google, factory, []);

        var health = await provider.CheckHealthAsync(TestContext.Current.CancellationToken);

        health.Status.ShouldBe(ModelProviderHealthStatus.Unknown);
        health.ProviderName.ShouldBe(GoogleProviderNames.Google);
    }

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
}
