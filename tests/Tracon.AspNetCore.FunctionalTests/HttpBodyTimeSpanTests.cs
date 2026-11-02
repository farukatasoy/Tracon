using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A time span in a request body is the clock <c>[days.]hh:mm:ss</c> the published schema describes. A bare number and an hour
/// above 23 are a <c>400</c> that names the field.
/// </summary>
/// <remarks>
/// <c>System.Text.Json</c> reads <c>"30"</c> as thirty days and <c>"00:30"</c> as thirty minutes, although the OpenAPI schema of
/// every time span says <c>^-?(\d+\.)?\d{2}:\d{2}:\d{2}(\.\d{1,7})?$</c>. A function node saved with <c>"timeout": "30"</c> got a
/// month. The read options every body goes through (<c>RequestBodyBinding</c>) carry the same reading as the configuration
/// keys do.
/// </remarks>
public sealed class HttpBodyTimeSpanTests
{
    [Theory]
    [InlineData("30")]
    [InlineData("00:30")]
    [InlineData("48:00:00")]
    [InlineData("30s")]
    [InlineData("")]
    public async Task A_node_timeout_that_is_not_a_clock_is_a_400_that_names_the_field(string timeout)
    {
        await using var host = await StartAsync();

        using var saved = await SaveAsync(host, new { kind = "Sequential", nodes = new object[] { new { name = "writer", kind = "Agent", timeout } } });

        saved.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await saved.Content.ReadAsStringAsync());

        var detail = await DetailAsync(saved);

        detail.ShouldContain("$.nodes[0].timeout");
        detail.ShouldContain("[days.]hh:mm:ss");
    }

    [Theory]
    [InlineData("runTimeout")]
    [InlineData("requestTimeout")]
    public async Task A_workflow_time_span_that_is_a_bare_number_is_a_400(string field)
    {
        await using var host = await StartAsync();

        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["kind"] = "Sequential",
            ["nodes"] = new object[] { new { name = "writer", kind = "Agent" } },
            [field] = "30",
        };

        using var saved = await SaveAsync(host, body);

        saved.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await saved.Content.ReadAsStringAsync());
        (await DetailAsync(saved)).ShouldContain($"$.{field}");
    }

    [Fact]
    public async Task A_time_span_written_as_a_number_is_still_a_400()
    {
        await using var host = await StartAsync();

        using var saved = await SaveAsync(host, new { kind = "Sequential", nodes = new object[] { new { name = "writer", kind = "Agent" } }, runTimeout = 30 });

        saved.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("00:00:30", "00:00:30")]
    [InlineData("0:00:30", "00:00:30")]
    [InlineData("00:06:00", "00:06:00")]
    [InlineData("1.00:00:00", "1.00:00:00")]
    [InlineData("00:00:02.5", "00:00:02.5000000")]
    public async Task A_clock_is_accepted_and_read_back_as_the_same_span(string written, string readBack)
    {
        await using var host = await StartAsync();

        using (var saved = await SaveAsync(host, new { kind = "Sequential", nodes = new object[] { new { name = "writer", kind = "Agent", timeout = written } } }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        var read = await host.Client.GetFromJsonAsync<JsonElement>("/tracon/api/workflows/chain", TestContext.Current.CancellationToken);

        read.GetProperty("nodes")[0].GetProperty("timeout").GetString().ShouldBe(readBack);
    }

    [Fact]
    public async Task An_absent_time_span_stays_absent()
    {
        await using var host = await StartAsync();

        using var saved = await SaveAsync(host, new { kind = "Sequential", nodes = new object[] { new { name = "writer", kind = "Agent" } } });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    private static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(builder => builder.AddAgent(TestData.Definition("writer")).UseWorkflows());

    private static Task<HttpResponseMessage> SaveAsync(TraconTestHost host, object body)
        => host.Client.PutAsJsonAsync("/tracon/api/workflows/chain", body);

    private static async Task<string> DetailAsync(HttpResponseMessage response)
        => (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString() ?? string.Empty;
}
