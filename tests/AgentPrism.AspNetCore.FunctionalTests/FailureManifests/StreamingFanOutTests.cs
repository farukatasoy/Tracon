using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

namespace AgentPrism.AspNetCore.FunctionalTests.FailureManifests;

/// <summary>
/// Failure manifest: <strong>many subscribers read one run's recorded
/// stream</strong> (Phase 157).
/// </summary>
/// <remarks>
/// <para>
/// The written expectation, verified below:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///       Every subscriber gets the <strong>same, complete</strong> sequence.
///       The event stream is served from the store, not from a live in-memory
///       broadcast, so a late subscriber is not a subscriber that missed
///       something - it reads the same rows from the beginning.
///     </description>
///   </item>
///   <item>
///     <description>
///       Fan-out costs one reader per subscriber. There is no shared cursor to
///       corrupt: subscribers do not interfere with each other, and one slow
///       or abandoned reader does not change what the others receive.
///     </description>
///   </item>
///   <item>
///     <description>
///       🚨 The cost model an operator must size for is therefore
///       <em>subscribers x events</em> of database reads, not one read shared
///       between them. Fan-out is a load question, not a correctness question.
///     </description>
///   </item>
/// </list>
/// <para>
/// 🚨 <strong>Scope.</strong> Every subscriber here opens on a run that has
/// already finished - the case where all of them must agree EXACTLY, and the
/// one an assertion can pin. A subscriber attached to a run still in flight
/// polls for new rows on top of the same per-subscriber reads; that tail is
/// covered by <c>StreamingTests</c>, not here.
/// </para>
/// </remarks>
public sealed class StreamingFanOutTests
{
    private const int SubscriberCount = 12;

    [Fact]
    public async Task Every_subscriber_reads_the_same_complete_sequence()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var runId = await RunAndGetRunIdAsync(host);

        var readers = Enumerable
            .Range(0, SubscriberCount)
            .Select(_ => ReadEventStreamAsync(host, runId))
            .ToList();

        var streams = await Task.WhenAll(readers);

        var reference = streams[0];
        reference.ShouldNotBeEmpty();

        foreach (var stream in streams)
        {
            stream.ShouldBe(reference, "every subscriber must see the same sequence, in the same order.");
        }
    }

    [Fact]
    public async Task An_abandoned_subscriber_does_not_change_what_the_others_receive()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var runId = await RunAndGetRunIdAsync(host);

        var expected = await ReadEventStreamAsync(host, runId);

        // Opened and dropped without reading to the end - the shape a
        // disconnected browser tab leaves behind.
        using (var abandoned = await host.Client.GetAsync(
            new Uri($"/agentprism/api/runs/{runId}/events", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead))
        {
            abandoned.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await ReadEventStreamAsync(host, runId)).ShouldBe(expected);
    }

    private static async Task<List<string>> ReadEventStreamAsync(AgentPrismTestHost host, Guid runId)
    {
        using var response = await host.Client.GetAsync(
            new Uri($"/agentprism/api/runs/{runId}/events", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        // Identity is the (id, event) pair: comparing the whole payload would
        // make the assertion fail on any field that legitimately differs per
        // read, and the claim here is about ORDER and COMPLETENESS.
        return frames
            .Select(frame => string.Create(CultureInfo.InvariantCulture, $"{frame.Id}:{frame.Event}"))
            .ToList();
    }

    private static async Task<Guid> RunAndGetRunIdAsync(AgentPrismTestHost host)
    {
        using (var run = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/agents/kod-agent/run", UriKind.Relative),
            new AgentRunRequest { Message = "hello" }))
        {
            await SseReader.ReadAllAsync(await run.Content.ReadAsStreamAsync());
        }

        using var runs = await host.Client.GetAsync(new Uri("/agentprism/api/runs", UriKind.Relative));
        var json = await AgentPrismTestHost.ReadJsonAsync(runs);

        json.GetArrayLength().ShouldBeGreaterThan(0);

        return json[0].GetProperty("id").GetGuid();
    }
}
