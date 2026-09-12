using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// End-to-end coverage of the quota threshold run-stream notice (phase 146):
/// off by default, ordering against the terminal event, parity between the
/// direct POST stream and <c>GET /api/runs/{id}/events</c>, Last-Event-ID
/// resumption, and durable per-period dedup.
/// </summary>
public sealed class QuotaRunNoticeTests
{
    private static readonly Uri Quotas = new("/tracon/api/quotas", UriKind.Relative);
    private static readonly Uri Runs = new("/tracon/api/runs", UriKind.Relative);

    [Fact]
    public async Task Default_off_produces_no_custom_frame()
    {
        await using var host = await StartAsync(publishToRunStream: false);
        await CreateQuotaAsync(host, maxRuns: 1);

        using var response = await PostRunAsync(host);
        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        frames.ShouldNotContain(static frame => string.Equals(frame.Event, "custom", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Enabled_notice_arrives_as_a_custom_frame_before_done()
    {
        await using var host = await StartAsync(publishToRunStream: true);
        await CreateQuotaAsync(host, maxRuns: 1);

        using var response = await PostRunAsync(host);
        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        var customIndex = frames.FindIndex(static frame => string.Equals(frame.Event, "custom", StringComparison.Ordinal));
        var doneIndex = frames.FindIndex(static frame => string.Equals(frame.Event, "done", StringComparison.Ordinal));

        customIndex.ShouldBeGreaterThanOrEqualTo(0);
        customIndex.ShouldBeLessThan(doneIndex);

        var payload = frames[customIndex].Data;
        payload.ShouldContain("\"type\":\"Custom\"", Case.Sensitive);
        payload.ShouldContain("\"customType\":\"tracon.quota.threshold\"", Case.Sensitive);

        // RunEvent.Payload is itself a JSON string nested inside the outer
        // RunEvent JSON, so its quotes come back re-escaped (", not a
        // literal '"') -- checking for the bare field names sidesteps that
        // and still proves the notice payload's fields survived the round trip.
        payload.ShouldContain("noticeId", Case.Sensitive);
        payload.ShouldContain("thresholdPercent", Case.Sensitive);
        payload.ShouldContain("resetsAt", Case.Sensitive);
    }

    [Fact]
    public async Task Direct_stream_and_the_events_endpoint_carry_the_same_notice()
    {
        await using var host = await StartAsync(publishToRunStream: true);
        await CreateQuotaAsync(host, maxRuns: 1);

        using var run = await PostRunAsync(host);
        var runFrames = await SseReader.ReadAllAsync(await run.Content.ReadAsStreamAsync());
        var direct = runFrames.Single(static frame => string.Equals(frame.Event, "custom", StringComparison.Ordinal));

        var runId = await LatestRunIdAsync(host);

        using var events = await host.Client.GetAsync(
            new Uri($"/tracon/api/runs/{runId}/events", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead);
        var eventFrames = await SseReader.ReadAllAsync(await events.Content.ReadAsStreamAsync());
        var replayed = eventFrames.Single(static frame => string.Equals(frame.Event, "custom", StringComparison.Ordinal));

        // Byte-for-byte: both paths read the SAME persisted RunEvent, serialized
        // the same way -- not two independently built copies (146, "HTTP endpoint'leri").
        replayed.Data.ShouldBe(direct.Data);
    }

    [Fact]
    public async Task Reconnecting_via_Last_Event_ID_still_reads_the_notice()
    {
        await using var host = await StartAsync(publishToRunStream: true);
        await CreateQuotaAsync(host, maxRuns: 1);

        using (var run = await PostRunAsync(host))
        {
            await SseReader.ReadAllAsync(await run.Content.ReadAsStreamAsync());
        }

        var runId = await LatestRunIdAsync(host);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/tracon/api/runs/{runId}/events");
        request.Headers.Add("Last-Event-ID", "0");

        using var resumed = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var frames = await SseReader.ReadAllAsync(await resumed.Content.ReadAsStreamAsync());

        frames.ShouldContain(static frame => string.Equals(frame.Event, "custom", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_second_run_that_crosses_no_new_threshold_gets_no_notice()
    {
        // maxRuns=2, a single 50% threshold: the FIRST run crosses it (50/50 ->
        // wait, 1/2 = 50%) and claims it; the SECOND run reaches 100%, which is
        // still >= 50, but the threshold was already claimed THIS period -- the
        // durable dedup (146.4) means no second notice, and it also proves the
        // notice one run's completion writes never leaks onto a DIFFERENT run's
        // own stream.
        await using var host = await StartAsync(publishToRunStream: true, thresholdPercents: [50]);
        await CreateQuotaAsync(host, maxRuns: 2);

        using (var first = await PostRunAsync(host))
        {
            var firstFrames = await SseReader.ReadAllAsync(await first.Content.ReadAsStreamAsync());
            firstFrames.ShouldContain(static frame => string.Equals(frame.Event, "custom", StringComparison.Ordinal));
        }

        using var second = await PostRunAsync(host);
        var secondFrames = await SseReader.ReadAllAsync(await second.Content.ReadAsStreamAsync());

        secondFrames.ShouldNotContain(static frame => string.Equals(frame.Event, "custom", StringComparison.Ordinal));
    }

    private static Task<TraconTestHost> StartAsync(bool publishToRunStream, IReadOnlyList<int>? thresholdPercents = null)
        => TraconTestHost.StartAsync(
            configureTracon: static builder => builder.AddAgent(TestData.Definition()),
            configureServices: services => services.Configure<TraconQuotaOptions>(options =>
            {
                options.PublishThresholdToRunStream = publishToRunStream;

                if (thresholdPercents is not null)
                {
                    options.ThresholdPercents.Clear();

                    foreach (var percent in thresholdPercents)
                    {
                        options.ThresholdPercents.Add(percent);
                    }
                }
            }));

    private static Task<HttpResponseMessage> PostRunAsync(TraconTestHost host)
        => host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/kod-agent/run", UriKind.Relative),
            new AgentRunRequest { Message = "merhaba" });

    private static async Task CreateQuotaAsync(TraconTestHost host, long maxRuns)
    {
        using var response = await host.Client.PutAsJsonAsync(
            Quotas,
            new QuotaSaveRequest
            {
                AgentName = "kod-agent",
                Period = QuotaPeriod.Daily,
                MaxRuns = maxRuns,
                Enabled = true,
            });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<Guid> LatestRunIdAsync(TraconTestHost host)
    {
        using var runs = await host.Client.GetAsync(Runs);
        var json = await TraconTestHost.ReadJsonAsync(runs);

        json.GetArrayLength().ShouldBeGreaterThan(0);

        return json[0].GetProperty("id").GetGuid();
    }
}
