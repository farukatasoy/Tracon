using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using FakeModelProvider = AgentPrism.Testing.FakeModelProvider;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Verifies the SSE streams: the run-agent stream and the run events stream.
/// </summary>
public sealed class StreamingTests
{
    [Fact]
    public async Task Run_streams_updates()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await PostRunAsync(host, new AgentRunRequest { Message = "merhaba" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        frames.ShouldContain(static frame => string.Equals(frame.Event, "update", StringComparison.Ordinal));
        frames[^1].Event.ShouldBe("done");
        frames.Where(static frame => string.Equals(frame.Event, "update", StringComparison.Ordinal))
            .Select(static frame => frame.Data)
            .ShouldContain(static data => data.Contains("Echo: merhaba", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Run_disables_reverse_proxy_buffering()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await PostRunAsync(host, new AgentRunRequest { Message = "merhaba" });

        response.Headers.GetValues("X-Accel-Buffering").ShouldContain(static value => string.Equals(value, "no", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_run_with_a_session_carries_history()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using (var first = await PostRunAsync(host, new AgentRunRequest { Message = "first", SessionId = "s-1" }))
        {
            await SseReader.ReadAllAsync(await first.Content.ReadAsStreamAsync());
        }

        using (var second = await PostRunAsync(host, new AgentRunRequest { Message = "second", SessionId = "s-1" }))
        {
            await SseReader.ReadAllAsync(await second.Content.ReadAsStreamAsync());
        }

        // The messages sent to the model on the second call must also include the first turn.
        var echo = host.Services.GetServices<IModelProvider>().OfType<FakeModelProvider>().Single();

        echo.Requests[^1].Messages
            .Select(static message => message.Text)
            .ShouldContain(static text => string.Equals(text, "first", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Nonexistent_agent_returns_404_before_the_stream_starts()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/agents/no-such-agent/run", UriKind.Relative),
            new AgentRunRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Empty_message_is_rejected()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await PostRunAsync(host, new AgentRunRequest { Message = "   " });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // --- Run events stream ---

    [Fact]
    public async Task Run_events_are_streamed_in_order()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var runId = await RunAndGetRunIdAsync(host);

        using var response = await host.Client.GetAsync(
            new Uri($"/agentprism/api/runs/{runId}/events", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead);

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        frames.ShouldNotBeEmpty();
        frames[0].Event.ShouldBe("run.started");
        frames[^1].Event.ShouldBe("run.completed");

        // Sequence numbers must be contiguous and increasing.
        var ids = frames.Select(static frame => long.Parse(frame.Id!, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        ids.ShouldBe(Enumerable.Range(0, ids.Count).Select(static i => (long)i).ToList());
    }

    [Fact]
    public async Task A_consumer_written_Custom_event_carries_its_CustomType_over_the_wire()
    {
        // Phase 141: the escape hatch reaches the client additively -- the
        // SAME endpoint serves both the live SSE tail and a finished run's
        // historical read (K-014), so this one test stands for both DoD rows
        // ("SSE carries customType" and "replay/historical read carries it").
        const string ToolName = "mark_preview_ready";

        await using var host = await AgentPrismTestHost.StartAsync(
            builder => builder
                .AddModelProvider(new FakeModelProvider("custom-event-model")
                    .CallsTool(ToolName)
                    .RespondsWith("done"))
                .AddTool(
                    AIFunctionFactory.Create(
                        async () =>
                        {
                            await AgentPrismRunContext.Current!.Writer!.AppendAsync(new RunEventDraft(RunEventType.Custom)
                            {
                                CustomType = "contoso.preview-ready",
                                Payload = """{"orderId":"ORD-7"}""",
                            });

                            return "preview ready";
                        },
                        ToolName))
                .AddAgent(TestData.Definition() with
                {
                    Model = new ModelBinding { Provider = "custom-event-model", Model = "custom-event-1" },
                    ToolNames = [ToolName],
                }));

        using var run = await PostRunAsync(host, new AgentRunRequest { Message = "prepare ORD-7" });
        await SseReader.ReadAllAsync(await run.Content.ReadAsStreamAsync());

        using var runs = await host.Client.GetAsync(new Uri("/agentprism/api/runs", UriKind.Relative));
        var runId = (await AgentPrismTestHost.ReadJsonAsync(runs))[0].GetProperty("id").GetGuid();

        using var response = await host.Client.GetAsync(
            new Uri($"/agentprism/api/runs/{runId}/events", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead);

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());
        var custom = frames
            .Where(static frame => frame.Data.Contains("\"type\":\"Custom\"", StringComparison.Ordinal))
            .ShouldHaveSingleItem();

        custom.Data.ShouldContain("\"customType\":\"contoso.preview-ready\"");

        // The payload itself, not just customType -- proves Payload is carried
        // through unexamined, the claim RunEventType.Custom's XML doc makes.
        custom.Data.ShouldContain("orderId");
        custom.Data.ShouldContain("ORD-7");
    }

    [Fact]
    public async Task Resumes_where_it_left_off_via_Last_Event_ID()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var runId = await RunAndGetRunIdAsync(host);

        List<SseFrame> all;

        using (var full = await host.Client.GetAsync(
            new Uri($"/agentprism/api/runs/{runId}/events", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead))
        {
            all = await SseReader.ReadAllAsync(await full.Content.ReadAsStreamAsync());
        }

        all.Count.ShouldBeGreaterThan(2);

        // The client received up through event number 1; the stream must resume from 2.
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/agentprism/api/runs/{runId}/events");
        request.Headers.Add("Last-Event-ID", "1");

        using var resumed = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var frames = await SseReader.ReadAllAsync(await resumed.Content.ReadAsStreamAsync());

        frames.Count.ShouldBe(all.Count - 2);
        frames[0].Id.ShouldBe("2");
        frames[^1].Event.ShouldBe(all[^1].Event);
    }

    [Fact]
    public async Task Nonexistent_runs_events_return_404()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(
            new Uri($"/agentprism/api/runs/{Guid.NewGuid()}/events", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    private static Task<HttpResponseMessage> PostRunAsync(AgentPrismTestHost host, AgentRunRequest request)
        => host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/agents/kod-agent/run", UriKind.Relative),
            request);

    /// <summary>Makes a run and returns the resulting run record's id.</summary>
    private static async Task<Guid> RunAndGetRunIdAsync(AgentPrismTestHost host)
    {
        using (var run = await PostRunAsync(host, new AgentRunRequest { Message = "merhaba" }))
        {
            await SseReader.ReadAllAsync(await run.Content.ReadAsStreamAsync());
        }

        using var runs = await host.Client.GetAsync(new Uri("/agentprism/api/runs", UriKind.Relative));
        var json = await AgentPrismTestHost.ReadJsonAsync(runs);

        json.GetArrayLength().ShouldBeGreaterThan(0);

        return json[0].GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Run_record_is_counted_in_the_summary()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        await RunAndGetRunIdAsync(host);

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/stats", UriKind.Relative));
        var json = await AgentPrismTestHost.ReadJsonAsync(response);

        json.GetProperty("totalRuns").GetInt64().ShouldBe(1);
        json.GetProperty("completedRuns").GetInt64().ShouldBe(1);
        json.GetProperty("errorRate").GetDouble().ShouldBe(0);
        json.GetProperty("byAgent")[0].GetProperty("agentName").GetString().ShouldBe("kod-agent");
    }

    [Fact]
    public async Task Empty_store_returns_a_zero_summary_and_a_null_error_rate()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/stats", UriKind.Relative));
        var json = await AgentPrismTestHost.ReadJsonAsync(response);

        json.GetProperty("totalRuns").GetInt64().ShouldBe(0);
        json.GetProperty("errorRate").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}
