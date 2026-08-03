using System.Net;
using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Faz 20 — <c>/api/stats</c> maliyet alanlari ve <c>/api/stats/timeseries</c>.
/// </summary>
public sealed class StatsCostTests
{
    [Fact]
    public async Task Stats_maliyet_alanlarini_tasir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/stats", UriKind.Relative));
        var json = await AgentPrismTestHost.ReadJsonAsync(response);

        // Bos depoda hic fiyatlandirilmis calistirma yoktur: toplam maliyet null,
        // tanimsiz sayaci sifirdir (sifir yazilir cunku hicbir model hic gorulmedi).
        json.GetProperty("totalCost").ValueKind.ShouldBe(JsonValueKind.Null);
        json.GetProperty("runsWithUnknownPricing").GetInt64().ShouldBe(0);
    }

    [Fact]
    public async Task Timeseries_bos_kovalari_doldurur()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var to = DateTimeOffset.UtcNow;
        var from = to.AddHours(-3);

        using var response = await host.Client.GetAsync(new Uri(
            $"/agentprism/api/stats/timeseries?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}&bucket=Hour",
            UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var points = await AgentPrismTestHost.ReadJsonAsync(response);

        // 3 saatlik aralik, saatlik kova: en az 3 kova doner (bos depoda hepsi sifir).
        points.GetArrayLength().ShouldBeGreaterThanOrEqualTo(3);
        points[0].GetProperty("runs").GetInt64().ShouldBe(0);
    }

    [Fact]
    public async Task Timeseries_ters_aralik_400_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var now = DateTimeOffset.UtcNow;

        using var response = await host.Client.GetAsync(new Uri(
            $"/agentprism/api/stats/timeseries?from={Uri.EscapeDataString(now.ToString("O"))}&to={Uri.EscapeDataString(now.AddHours(-1).ToString("O"))}",
            UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Timeseries_kova_sinirini_asinca_400_ve_oneri_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var to = DateTimeOffset.UtcNow;
        var from = to.AddDays(-30);

        using var response = await host.Client.GetAsync(new Uri(
            $"/agentprism/api/stats/timeseries?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}&bucket=Hour",
            UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var json = await AgentPrismTestHost.ReadJsonAsync(response);
        var detail = json.GetProperty("detail").GetString();
        detail.ShouldNotBeNull();
        detail.ShouldContain("500");
    }
}
