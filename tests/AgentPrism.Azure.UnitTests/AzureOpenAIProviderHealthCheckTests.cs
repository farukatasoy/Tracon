using System.Net;
using System.Text;
using AgentPrism.Azure.UnitTests.Infrastructure;

namespace AgentPrism.Azure.UnitTests;

/// <summary>
/// Saglik denetiminin adres birlestirmesi, yanit ayristirmasi ve kimlik secimi.
/// Gercek bir Azure kaynagina cagri yapilmaz.
/// </summary>
public sealed class AzureOpenAIProviderHealthCheckTests
{
    [Fact]
    public void Egik_cizgili_taban_adres_veri_duzlemi_yoluna_baglanir()
        => AzureOpenAIProviderHealthCheck.BuildModelsEndpoint(TestData.Endpoint)
            .ToString().ShouldBe(
                $"{TestData.EndpointText}openai/models?api-version={AzureOpenAIProviderHealthCheck.ApiVersion}");

    [Fact]
    public void Egik_cizgisiz_taban_adres_son_parcayi_yutmaz()
        => AzureOpenAIProviderHealthCheck.BuildModelsEndpoint(new Uri("https://ornek.gecit/azure"))
            .ToString().ShouldBe(
                $"https://ornek.gecit/azure/openai/models?api-version={AzureOpenAIProviderHealthCheck.ApiVersion}");

    [Fact]
    public async Task Yanittan_model_kimlikleri_okunur_ve_siralanir()
    {
        using var response = Json("""
            {"data":[{"id":"gpt-5.6-terra"},{"id":"gpt-4o-mini"},{"id":"text-embedding-3-large"}]}
            """);

        var models = await AzureOpenAIProviderHealthCheck.ReadModelIdsAsync(response, TestContext.Current.CancellationToken);

        models.ShouldBe(["gpt-4o-mini", "gpt-5.6-terra", "text-embedding-3-large"]);
    }

    [Fact]
    public async Task Beklenmeyen_govde_bos_liste_dondurur()
    {
        using var response = Json("""{"nesne":"list"}""");

        var models = await AzureOpenAIProviderHealthCheck.ReadModelIdsAsync(response, TestContext.Current.CancellationToken);

        models.ShouldBeEmpty();
    }

    [Fact]
    public async Task Kimliksiz_ogeler_atlanir()
    {
        using var response = Json("""{"data":[{"nesne":"model"},{"id":""},{"id":"gpt-5.6-terra"}]}""");

        var models = await AzureOpenAIProviderHealthCheck.ReadModelIdsAsync(response, TestContext.Current.CancellationToken);

        models.ShouldBe(["gpt-5.6-terra"]);
    }

    [Fact]
    public async Task Baglanamayan_ucun_detayinda_ne_anahtar_ne_adres_gorunur()
    {
        // Kapali bir port: baglanti reddedilir. HttpRequestException.Message hedef
        // adresi govdeye gomerdi; HttpRequestError kategorisi adres tasimaz.
        var health = await CheckAsync(o => o.Endpoint = new Uri("http://127.0.0.1:1/"));

        health.Status.ShouldBe(ModelProviderHealthStatus.Unhealthy);
        health.Detail.ShouldNotBeNull();
        health.Detail.ShouldNotContain(TestData.ApiKey);
        health.Detail.ShouldNotContain("127.0.0.1");
    }

    [Fact]
    public async Task Adres_tanimsizsa_denetim_cagri_yapmadan_saglıksiz_doner()
    {
        var health = await CheckAsync(o => o.Endpoint = null);

        health.Status.ShouldBe(ModelProviderHealthStatus.Unhealthy);
        health.Latency.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public async Task Kimlik_fabrikasi_varsa_genel_bulut_kapsami_istenir()
    {
        var credential = new SahteTokenKimligi();

        await CheckAsync(o =>
        {
            o.Endpoint = new Uri("http://127.0.0.1:1/");
            o.CredentialFactory = () => credential;
        });

        credential.SonKapsam.ShouldBe(AzureOpenAIProviderHealthCheck.DefaultAudience);
    }

    [Fact]
    public async Task Egemen_bulut_kapsami_ayardan_okunur()
    {
        var credential = new SahteTokenKimligi();
        const string Kapsam = "https://cognitiveservices.azure.us/.default";

        await CheckAsync(o =>
        {
            o.Endpoint = new Uri("http://127.0.0.1:1/");
            o.Audience = Kapsam;
            o.CredentialFactory = () => credential;
        });

        credential.SonKapsam.ShouldBe(Kapsam);
    }

    [Fact]
    public async Task Kimlik_denetim_basina_bir_kez_kurulur()
    {
        var credential = new SahteTokenKimligi();
        var kurulumSayisi = 0;

        var check = new AzureOpenAIProviderHealthCheck(
            AzureOpenAIProviderNames.AzureOpenAI,
            TestData.Options(o =>
            {
                o.Endpoint = new Uri("http://127.0.0.1:1/");
                o.CredentialFactory = () => { kurulumSayisi++; return credential; };
            }));

        await check.CheckHealthAsync(TestContext.Current.CancellationToken);
        await check.CheckHealthAsync(TestContext.Current.CancellationToken);

        // Kimlik NESNESI bir kez kurulur (token onbellegi korunur), ama her
        // denetimde yeni bir token istenir.
        kurulumSayisi.ShouldBe(1);
        credential.IstenenTokenSayisi.ShouldBe(2);
    }

    [Fact]
    public async Task Saglik_ayari_verilmeyen_saglayici_bilinmiyor_dondurur()
    {
        var provider = new AzureOpenAIModelProvider(
            AzureOpenAIProviderNames.AzureOpenAI,
            new AzureOpenAIChatClientFactory(TestData.Options()),
            []);

        var health = await provider.CheckHealthAsync(TestContext.Current.CancellationToken);

        health.Status.ShouldBe(ModelProviderHealthStatus.Unknown);
        health.ProviderName.ShouldBe(AzureOpenAIProviderNames.AzureOpenAI);
    }

    private static ValueTask<ModelProviderHealth> CheckAsync(Action<AzureOpenAIProviderOptions> configure)
    {
        var options = TestData.Options(o =>
        {
            o.Timeout = TimeSpan.FromSeconds(5);
            configure(o);
        });

        return new AzureOpenAIProviderHealthCheck(AzureOpenAIProviderNames.AzureOpenAI, options)
            .CheckHealthAsync(TestContext.Current.CancellationToken);
    }

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
}
