using System.Net;
using System.Text;
using AgentPrism.Anthropic.UnitTests.Infrastructure;

namespace AgentPrism.Anthropic.UnitTests;

/// <summary>
/// Saglik denetiminin adres birlestirmesi ve yanit ayristirmasi. Ag cagrisi yapilmaz.
/// </summary>
public sealed class AnthropicProviderHealthCheckTests
{
    [Fact]
    public void Adres_verilmezse_resmi_uc_kullanilir()
        => AnthropicProviderHealthCheck.BuildModelsEndpoint(null)
            .ToString().ShouldBe("https://api.anthropic.com/v1/models");

    [Fact]
    public void Egik_cizgisiz_taban_adres_son_parcayi_yutmaz()
        => AnthropicProviderHealthCheck.BuildModelsEndpoint(new Uri("https://ornek.gecit/v1"))
            .ToString().ShouldBe("https://ornek.gecit/v1/models");

    [Fact]
    public void Egik_cizgili_taban_adres_ayni_sonucu_verir()
        => AnthropicProviderHealthCheck.BuildModelsEndpoint(new Uri("https://ornek.gecit/v1/"))
            .ToString().ShouldBe("https://ornek.gecit/v1/models");

    [Fact]
    public async Task Yanittan_model_kimlikleri_okunur_ve_siralanir()
    {
        using var response = Json("""
            {"data":[{"id":"claude-sonnet-5"},{"id":"claude-haiku-4-5-20251001"},{"id":"claude-opus-5"}]}
            """);

        var models = await AnthropicProviderHealthCheck.ReadModelIdsAsync(response, TestContext.Current.CancellationToken);

        models.ShouldBe(["claude-haiku-4-5-20251001", "claude-opus-5", "claude-sonnet-5"]);
    }

    [Fact]
    public async Task Beklenmeyen_govde_bos_liste_dondurur()
    {
        using var response = Json("""{"nesne":"list"}""");

        var models = await AnthropicProviderHealthCheck.ReadModelIdsAsync(response, TestContext.Current.CancellationToken);

        models.ShouldBeEmpty();
    }

    [Fact]
    public async Task Kimliksiz_ogeler_atlanir()
    {
        using var response = Json("""{"data":[{"nesne":"model"},{"id":""},{"id":"claude-opus-5"}]}""");

        var models = await AnthropicProviderHealthCheck.ReadModelIdsAsync(response, TestContext.Current.CancellationToken);

        models.ShouldBe(["claude-opus-5"]);
    }

    [Fact]
    public async Task Baglanamayan_ucun_detayinda_ne_anahtar_ne_adres_gorunur()
    {
        // Kapali bir port: baglanti reddedilir. HttpRequestException.Message hedef
        // adresi govdeye gomerdi; HttpRequestError kategorisi adres tasimaz.
        var options = TestData.Options(o =>
        {
            o.Endpoint = new Uri("http://127.0.0.1:1/v1");
            o.Timeout = TimeSpan.FromSeconds(5);
        });

        var health = await new AnthropicProviderHealthCheck(AnthropicProviderNames.Anthropic, options)
            .CheckHealthAsync(TestContext.Current.CancellationToken);

        health.Status.ShouldBe(ModelProviderHealthStatus.Unhealthy);
        health.Detail.ShouldNotBeNull();
        health.Detail.ShouldNotContain(TestData.ApiKey);
        health.Detail.ShouldNotContain("127.0.0.1");
    }

    [Fact]
    public async Task Saglik_ayari_verilmeyen_saglayici_bilinmiyor_dondurur()
    {
        var provider = new AnthropicModelProvider(
            AnthropicProviderNames.Anthropic,
            new AnthropicChatClientFactory(TestData.Options()),
            []);

        var health = await provider.CheckHealthAsync(TestContext.Current.CancellationToken);

        health.Status.ShouldBe(ModelProviderHealthStatus.Unknown);
        health.ProviderName.ShouldBe(AnthropicProviderNames.Anthropic);
    }

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
}
