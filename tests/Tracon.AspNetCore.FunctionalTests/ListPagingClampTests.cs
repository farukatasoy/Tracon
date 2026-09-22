using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// <c>GET /api/sessions</c> and <c>GET /api/runs</c> CLAMP their paging
/// parameters instead of rejecting them.
/// </summary>
/// <remarks>
/// <para>
/// Both endpoints promise, in their own OpenAPI description, that an
/// out-of-range <c>take</c> "never fails the request". That promise had no test
/// on either endpoint: a rewrite that swapped <c>Math.Clamp</c> for a validation
/// branch would have turned a working client into a wall of 400s, and a rewrite
/// that dropped the clamp entirely would have let <c>take=99999</c> pull the
/// whole table.
/// </para>
/// <para>
/// The assertions are exact row counts, not "does not crash": the seeded set is
/// two rows, so a clamp to 1 is visible and an ignored <c>take</c> is visible too.
/// </para>
/// </remarks>
public sealed class ListPagingClampTests
{
    [Theory]
    [InlineData("/tracon/api/sessions")]
    [InlineData("/tracon/api/runs")]
    public async Task Take_below_the_floor_is_clamped_to_one_row(string path)
    {
        await using var host = await StartWithTwoRowsAsync();

        (await CountAsync(host, $"{path}?take=0")).ShouldBe(1);
    }

    [Theory]
    [InlineData("/tracon/api/sessions")]
    [InlineData("/tracon/api/runs")]
    public async Task Take_above_the_ceiling_is_clamped_and_still_returns_the_page(string path)
    {
        await using var host = await StartWithTwoRowsAsync();

        // The ceiling is 200 and only two rows exist, so the clamp is not
        // observable in the count here. What IS observable is that the request
        // succeeds: rejecting it with a 400 is the failure this guards.
        (await CountAsync(host, $"{path}?take=99999")).ShouldBe(2);
    }

    [Theory]
    [InlineData("/tracon/api/sessions")]
    [InlineData("/tracon/api/runs")]
    public async Task Negative_skip_is_treated_as_zero(string path)
    {
        await using var host = await StartWithTwoRowsAsync();

        // A negative skip reaching the store would be an argument exception
        // there, surfacing as a 500.
        (await CountAsync(host, $"{path}?skip=-5")).ShouldBe(2);
    }

    private static async Task<TraconTestHost> StartWithTwoRowsAsync()
    {
        var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        // One run per session: two session rows and two run rows from the same
        // seed, so the two endpoints are measured against the same expectation.
        await RunAsync(host, "one", "paging-a");
        await RunAsync(host, "two", "paging-b");

        return host;
    }

    private static async Task RunAsync(TraconTestHost host, string message, string sessionId)
    {
        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/kod-agent/run", UriKind.Relative),
            new AgentRunRequest { Message = message, SessionId = sessionId });

        response.EnsureSuccessStatusCode();

        await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());
    }

    private static async Task<int> CountAsync(TraconTestHost host, string path)
    {
        using var response = await host.Client.GetAsync(new Uri(path, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await TraconTestHost.ReadJsonAsync(response)).GetArrayLength();
    }
}
