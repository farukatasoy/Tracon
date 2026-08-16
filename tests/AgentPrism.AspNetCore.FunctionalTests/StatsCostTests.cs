using System.Net;
using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 20 — <c>/api/stats</c> cost fields and <c>/api/stats/timeseries</c>.
/// </summary>
public sealed class StatsCostTests
{
    [Fact]
    public async Task Stats_carries_cost_fields()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/stats", UriKind.Relative));
        var json = await AgentPrismTestHost.ReadJsonAsync(response);

        // An empty store has no priced runs at all: total cost is null, and
        // the unknown-pricing counter is zero (zero is written because no
        // model was ever seen).
        json.GetProperty("totalCost").ValueKind.ShouldBe(JsonValueKind.Null);
        json.GetProperty("runsWithUnknownPricing").GetInt64().ShouldBe(0);
    }

    [Fact]
    public async Task Timeseries_fills_empty_buckets()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var to = DateTimeOffset.UtcNow;
        var from = to.AddHours(-3);

        using var response = await host.Client.GetAsync(new Uri(
            $"/agentprism/api/stats/timeseries?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}&bucket=Hour",
            UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var points = await AgentPrismTestHost.ReadJsonAsync(response);

        // A 3-hour range, hourly bucket: at least 3 buckets come back (all zero in an empty store).
        points.GetArrayLength().ShouldBeGreaterThanOrEqualTo(3);
        points[0].GetProperty("runs").GetInt64().ShouldBe(0);
    }

    [Fact]
    public async Task Timeseries_reversed_range_returns_400()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var now = DateTimeOffset.UtcNow;

        using var response = await host.Client.GetAsync(new Uri(
            $"/agentprism/api/stats/timeseries?from={Uri.EscapeDataString(now.ToString("O"))}&to={Uri.EscapeDataString(now.AddHours(-1).ToString("O"))}",
            UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Timeseries_returns_400_and_a_suggestion_when_the_bucket_limit_is_exceeded()
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
