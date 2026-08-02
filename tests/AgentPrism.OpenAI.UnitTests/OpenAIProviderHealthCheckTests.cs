using System.Net;
using System.Text;

namespace AgentPrism.OpenAI.UnitTests;

/// <summary>
/// <see cref="OpenAIProviderHealthCheck"/>'in ag cagrisi gerektirmeyen saf
/// parcalarini dogrular: adres birlestirme ve JSON ayristirma.
/// </summary>
/// <remarks>
/// Tam uctan uca akis (gercek HTTP GET, zaman asimi, hata govdesi) fonksiyonel
/// testte gercek bir yerel sunucuya karsi dogrulanir — bkz.
/// <c>ModelHealthEndpointsTests</c> ve <c>OpenAiCompatibleLocalServerTests</c>.
/// </remarks>
public sealed class OpenAIProviderHealthCheckTests
{
    [Theory]
    [InlineData("https://openrouter.example.com/api/v1", "https://openrouter.example.com/api/v1/models")]
    [InlineData("https://openrouter.example.com/api/v1/", "https://openrouter.example.com/api/v1/models")]
    [InlineData("http://localhost:11434/v1", "http://localhost:11434/v1/models")]
    public void Taban_adres_egik_cizgiden_bagimsiz_dogru_birlesir(string baseEndpoint, string expected)
    {
        var result = OpenAIProviderHealthCheck.BuildModelsEndpoint(new Uri(baseEndpoint));

        result.ShouldBe(new Uri(expected));
    }

    [Fact]
    public void Adres_verilmezse_resmi_OpenAI_adresi_kullanilir()
    {
        var result = OpenAIProviderHealthCheck.BuildModelsEndpoint(null);

        result.ShouldBe(new Uri("https://api.openai.com/v1/models"));
    }

    [Fact]
    public async Task Model_kimlikleri_data_dizisinden_siral_okunur()
    {
        using var response = JsonResponse("""{"data":[{"id":"b"},{"id":"a"}]}""");

        var models = await OpenAIProviderHealthCheck.ReadModelIdsAsync(response, CancellationToken.None);

        models.ShouldBe(["a", "b"]);
    }

    [Fact]
    public async Task Data_alani_yoksa_bos_liste_doner()
    {
        using var response = JsonResponse("""{"object":"list"}""");

        var models = await OpenAIProviderHealthCheck.ReadModelIdsAsync(response, CancellationToken.None);

        models.ShouldBeEmpty();
    }

    [Fact]
    public async Task Data_dizi_degilse_bos_liste_doner()
    {
        using var response = JsonResponse("""{"data":"beklenmedik"}""");

        var models = await OpenAIProviderHealthCheck.ReadModelIdsAsync(response, CancellationToken.None);

        models.ShouldBeEmpty();
    }

    [Fact]
    public async Task Id_alani_olmayan_girdiler_atlanir()
    {
        using var response = JsonResponse("""{"data":[{"id":"gecerli"},{"owned_by":"x"},{"id":123}]}""");

        var models = await OpenAIProviderHealthCheck.ReadModelIdsAsync(response, CancellationToken.None);

        models.ShouldBe(["gecerli"]);
    }

    [Fact]
    public async Task Model_sayisi_200_ile_sinirlanir()
    {
        var entries = string.Join(',', Enumerable.Range(0, 250).Select(static i => $$"""{"id":"model-{{i}}"}"""));
        using var response = JsonResponse($$"""{"data":[{{entries}}]}""");

        var models = await OpenAIProviderHealthCheck.ReadModelIdsAsync(response, CancellationToken.None);

        models.Count.ShouldBe(200);
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
}
