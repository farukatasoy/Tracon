using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The HTTP contract of <c>GET /api/stats/errors</c> and the counter half of
/// <c>GET /api/stats</c>.
/// </summary>
/// <remarks>
/// <para>
/// Both endpoints read through <c>IRunStore.GetStatisticsAsync</c>, so the
/// grouping itself has store-level coverage. What had none was the HTTP layer:
/// that <c>/api/stats/errors</c> returns the <c>ByErrorClass</c> slice, that the
/// class is written as a NAME and not an ordinal, and that <c>?hours=</c> really
/// narrows the window. A run that fails is produced through the real endpoint
/// with <see cref="ThrowingModelProvider"/>, never by writing a run row by hand —
/// the point is that a provider failure survives classification, recording and
/// serialization end to end.
/// </para>
/// <para>
/// Cost fields are deliberately untouched here; they belong to the
/// observability family.
/// </para>
/// </remarks>
public sealed class StatsErrorEndpointTests
{
    private const string AgentName = "broken-agent";
    private const string IdempotencyHeader = "Idempotency-Key";

    private static readonly Uri Stats = new("/tracon/api/stats?agentName=broken-agent", UriKind.Relative);
    private static readonly Uri StatsErrors = new("/tracon/api/stats/errors?agentName=broken-agent", UriKind.Relative);

    [Fact]
    public async Task Repeated_provider_failures_are_grouped_under_one_error_class()
    {
        await using var host = await TraconTestHost.StartAsync(ConfigureBrokenAgent);

        await FailARunAsync(host);
        await FailARunAsync(host);

        using var response = await host.Client.GetAsync(StatsErrors);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var classes = (await TraconTestHost.ReadJsonAsync(response)).EnumerateArray().ToList();

        classes.ShouldNotBeEmpty();

        // The class is written as a NAME. An ordinal would still deserialize on
        // the generated client and still look green in a round-trip test, but no
        // operator reading /api/stats/errors could tell 3 from 4.
        var only = classes.ShouldHaveSingleItem();
        only.GetProperty("class").ValueKind.ShouldBe(JsonValueKind.String);
        only.GetProperty("class").GetString().ShouldNotBeNullOrWhiteSpace();

        // Both failures share a class AND a fingerprint: same provider, same
        // message. Grouping that split them into two rows of one would satisfy
        // "at least one entry" while defeating the purpose of the endpoint.
        only.GetProperty("totalRuns").GetInt64().ShouldBe(2);

        var cluster = only.GetProperty("topClusters").EnumerateArray().ToList().ShouldHaveSingleItem();
        cluster.GetProperty("count").GetInt64().ShouldBe(2);
        cluster.GetProperty("fingerprint").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Narrowing_the_hours_window_drops_older_failures()
    {
        // The window is resolved from the injected TimeProvider, so advancing a
        // fake clock after the runs are recorded is what "an hour later" means
        // here. A wall-clock sleep would be the same assertion made slowly and
        // flakily.
        var clock = new ManualTimeProvider();

        await using var host = await TraconTestHost.StartAsync(
            ConfigureBrokenAgent,
            configureServices: services => services.AddSingleton<TimeProvider>(clock));

        await FailARunAsync(host);

        clock.Advance(TimeSpan.FromHours(2));

        (await TotalRunsAsync(host, StatsErrors)).ShouldBe(1);

        // 0.01 hours is 36 seconds; the failure is two hours old by now.
        (await TotalRunsAsync(
            host,
            new Uri("/tracon/api/stats/errors?agentName=broken-agent&hours=0.01", UriKind.Relative)))
            .ShouldBe(0);
    }

    [Fact]
    public async Task Stats_counters_add_up_and_the_agent_appears_in_the_breakdown()
    {
        await using var host = await TraconTestHost.StartAsync(ConfigureBrokenAgent);

        await FailARunAsync(host);
        await FailARunAsync(host);

        using var response = await host.Client.GetAsync(Stats);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await TraconTestHost.ReadJsonAsync(response);

        json.GetProperty("failedRuns").GetInt64().ShouldBe(2);

        // The six counters partition the runs: a status that stopped being
        // counted anywhere would leave totalRuns above the sum, and a run
        // counted twice would push it below.
        var parts = json.GetProperty("completedRuns").GetInt64()
            + json.GetProperty("failedRuns").GetInt64()
            + json.GetProperty("canceledRuns").GetInt64()
            + json.GetProperty("runningRuns").GetInt64()
            + json.GetProperty("awaitingInputRuns").GetInt64();

        json.GetProperty("totalRuns").GetInt64().ShouldBe(parts);

        json.GetProperty("byAgent").EnumerateArray()
            .Select(static agent => agent.GetProperty("agentName").GetString())
            .ShouldContain(static name => name == AgentName);
    }

    private static void ConfigureBrokenAgent(ITraconBuilder builder)
    {
        builder
            .AddModelProvider(new ThrowingModelProvider())
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Give a short answer.",
                Model = new ModelBinding { Provider = "kirik", Model = "kirik-1" },
            });
    }

    /// <summary>Runs the broken agent once and asserts the failure reached the wire.</summary>
    private static async Task FailARunAsync(TraconTestHost host)
    {
        // The Idempotency-Key header is what selects the non-streaming branch of
        // the run endpoint; without it the response is an SSE stream and the
        // failure arrives as an error frame instead of a status code.
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }),
        };
        request.Headers.Add(IdempotencyHeader, Guid.NewGuid().ToString("N"));

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
    }

    private static async Task<long> TotalRunsAsync(TraconTestHost host, Uri uri)
    {
        using var response = await host.Client.GetAsync(uri);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await TraconTestHost.ReadJsonAsync(response)).EnumerateArray()
            .Sum(static entry => entry.GetProperty("totalRuns").GetInt64());
    }
}
