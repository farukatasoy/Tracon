using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using AgentPrism.Client.Generated;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Proves that the NSwag-generated client's SSE-returning methods actually
/// work against a real server (Phase 145, independent-audit finding; extended
/// by Phase 159).
/// </summary>
/// <remarks>
/// <para>
/// Before Phase 145's fix (<c>nswag-postprocess-client.py</c>'s fourth rewrite
/// pass), every generated method for a pure <c>text/event-stream</c> 200
/// response read its body through the SAME JSON-deserializing helper every
/// other operation uses — which threw <see cref="AgentPrismApiException"/>
/// unconditionally, because a raw SSE frame is not a JSON string literal.
/// </para>
/// <para>
/// Phase 159 added the sixth pass: every operation declaring an SSE 200 now
/// also has a <c>&lt;operation&gt;StreamAsync</c> sibling returning
/// <c>IAsyncEnumerable&lt;string&gt;</c>. That closed the last two endpoints
/// that were not callable in their streaming shape at all —
/// <c>/v1/responses</c> and <c>/v1/chat/completions</c> pick JSON or SSE from
/// the <c>stream</c> flag in the request body at run time, which no OpenAPI
/// document can describe.
/// </para>
/// <para>
/// Nothing in the repository called these methods against a real host, so the
/// defects were invisible to <c>dotnet build</c> and to
/// <see cref="ClientCoverageTests"/> (which only checks that a method exists,
/// not that a call to it succeeds) — exactly the gap
/// <c>AgentPrismPublicApiTrackingEnabled=false</c> leaves for a generated
/// method's runtime behavior.
/// </para>
/// </remarks>
public sealed class GeneratedClientSseTests
{
    // --- Phase 145: the pure-SSE methods read their body as text ---

    [Fact]
    public async Task AgentPrismRunAgentAsync_reads_the_SSE_body_as_plain_text()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var client = CreateClient(host);

        var body = await client.AgentPrismRunAgentAsync("kod-agent", RunRequest());

        body.ShouldContain("event: run");
        body.ShouldContain("event: done");
    }

    [Fact]
    public async Task AgentPrismStreamRunEventsAsync_reads_the_SSE_body_as_plain_text()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var client = CreateClient(host);

        await client.AgentPrismRunAgentAsync("kod-agent", RunRequest());

        var runs = await client.AgentPrismListRunsAsync();
        var runId = runs.Single().Id;

        var body = await client.AgentPrismStreamRunEventsAsync(runId);

        body.ShouldContain("event: run.started");
        body.ShouldContain("event: run.completed");
        body.ShouldNotContain("event: unknown");
    }

    // --- Phase 159: the dual JSON/SSE endpoints, in both shapes ---

    [Fact]
    public async Task OpenAI_responses_streams_frames_when_the_body_asks_for_a_stream()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var client = CreateClient(host);

        var frames = new List<string>();

        await foreach (var frame in client.AgentPrismOpenAIResponsesStreamAsync(
            Body(new { model = "kod-agent", input = "hello", stream = true })))
        {
            frames.Add(frame);
        }

        frames.ShouldNotBeEmpty();
        frames.ShouldContain(static frame => frame.Contains("event: response.created", StringComparison.Ordinal));
        frames.ShouldContain(static frame => frame.Contains("event: response.completed", StringComparison.Ordinal));

        // Each element is ONE frame, not the whole buffered body.
        frames.Count.ShouldBeGreaterThan(1);
        frames.ShouldAllBe(static frame => !frame.EndsWith('\n'));
    }

    [Fact]
    public async Task OpenAI_chat_completions_streams_frames_ending_with_the_DONE_marker()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var client = CreateClient(host);

        var frames = new List<string>();

        await foreach (var frame in client.AgentPrismOpenAIChatCompletionsStreamAsync(
            Body(new
            {
                model = "kod-agent",
                stream = true,
                messages = new[] { new { role = "user", content = "hello" } },
            })))
        {
            frames.Add(frame);
        }

        frames[0].ShouldContain("chat.completion.chunk");
        frames[^1].ShouldBe("data: [DONE]");
        frames.ShouldContain(static frame => frame.Contains("Echo: hello", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OpenAI_responses_still_returns_JSON_when_the_body_does_not_ask_for_a_stream()
    {
        // The pre-existing shape is preserved exactly: adding the streaming
        // sibling must not change what the JSON method returns.
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var client = CreateClient(host);

        var json = await client.AgentPrismOpenAIResponsesAsync(
            Body(new { model = "kod-agent", input = "hello", stream = false }));

        json.GetProperty("object").GetString().ShouldBe("response");
        json.GetProperty("status").GetString().ShouldBe("completed");
        json.GetProperty("output")[0].GetProperty("content")[0].GetProperty("text").GetString()
            .ShouldBe("Echo: hello");
    }

    // --- Phase 159: method and body disagreeing, in both directions ---

    [Fact]
    public async Task The_JSON_method_names_the_streaming_sibling_when_the_body_asked_for_a_stream()
    {
        // Before this, the server answered SSE and NSwag's JSON helper failed
        // with "Could not deserialize the response body stream as
        // System.Text.Json.JsonElement" — true, but naming neither the cause
        // nor the fix.
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var client = CreateClient(host);

        var exception = await Should.ThrowAsync<AgentPrismApiException>(
            async () => await client.AgentPrismOpenAIResponsesAsync(
                Body(new { model = "kod-agent", input = "hello", stream = true })));

        exception.Message.ShouldContain("text/event-stream");
        exception.Message.ShouldContain("AgentPrismOpenAIResponsesStreamAsync");
    }

    [Fact]
    public async Task The_streaming_method_names_the_JSON_sibling_when_the_body_did_not_ask_for_a_stream()
    {
        // The client does NOT rewrite the caller's body to match the method.
        // Silently changing what was asked for is a worse surprise than a
        // clear failure — and the alternative here is worse still: an SSE
        // parser finds no frames in a JSON document, so the call would
        // otherwise succeed with a silently EMPTY stream.
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var client = CreateClient(host);

        var exception = await Should.ThrowAsync<AgentPrismApiException>(async () =>
        {
            await foreach (var _ in client.AgentPrismOpenAIResponsesStreamAsync(
                Body(new { model = "kod-agent", input = "hello", stream = false })))
            {
                // Never reached: the guard fires before the first frame.
            }
        });

        exception.Message.ShouldContain("application/json");
        exception.Message.ShouldContain("AgentPrismOpenAIResponsesAsync");
        // The JSON the server actually answered with is attached, so the caller
        // can see what came back instead of guessing.
        exception.Response.ShouldNotBeNull();
        exception.Response.ShouldContain("\"object\":\"response\"");
    }

    [Fact]
    public async Task An_uninitialized_JsonElement_body_is_named_rather_than_failing_inside_the_serializer()
    {
        // MEASURED (2026-09-08): System.Text.Json otherwise throws
        // "InvalidOperationException: Operation is not valid due to the
        // current state of the object" — no parameter, no method, no cause.
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var client = CreateClient(host);

        var exception = await Should.ThrowAsync<ArgumentException>(
            async () => await client.AgentPrismOpenAIResponsesAsync(default));

        exception.ParamName.ShouldBe("body");
        exception.Message.ShouldContain("uninitialized JsonElement");
    }

    // --- Phase 159: the pure-SSE endpoints gained the same streaming shape ---

    [Fact]
    public async Task A_pure_SSE_endpoint_streams_frames_through_its_sibling()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var client = CreateClient(host);

        var frames = new List<string>();

        await foreach (var frame in client.AgentPrismRunAgentStreamAsync("kod-agent", RunRequest()))
        {
            frames.Add(frame);
        }

        frames.ShouldNotBeEmpty();

        // SseWriter puts the `id:` line first, so a frame's own event name is a
        // LINE of the raw text, not its prefix.
        frames.ShouldContain(static frame => frame.Contains("\nevent: done", StringComparison.Ordinal));

        // The frames carry the same content the buffered method returns as one
        // string — one contract, two shapes.
        string.Join("\n\n", frames).ShouldContain("event: run");
    }

    [Fact]
    public async Task Breaking_out_early_stops_the_stream_without_faulting()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var client = CreateClient(host);

        var seen = 0;

        await foreach (var _ in client.AgentPrismRunAgentStreamAsync("kod-agent", RunRequest()))
        {
            seen++;
            break;
        }

        // Leaving the loop early disposes the enumerator, which runs the
        // generated method's own finally blocks and releases the response.
        seen.ShouldBe(1);
    }

    [Fact]
    public async Task WithCancellation_reaches_the_request_the_streaming_method_sends()
    {
        // The [EnumeratorCancellation] attribute on the generated parameter is
        // what makes this work. Without it the token passed to WithCancellation
        // is silently ignored and the call runs to completion.
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var client = CreateClient(host);

        using var cts = new CancellationTokenSource();

        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in client
                .AgentPrismRunAgentStreamAsync("kod-agent", RunRequest())
                .WithCancellation(cts.Token))
            {
                // Never reached: the request is cancelled before it is sent.
            }
        });
    }

    private static JsonElement Body(object value) => JsonSerializer.SerializeToElement(value);

    private static AgentPrismApiClient CreateClient(AgentPrismTestHost host)
    {
        // The generated client's paths are relative to the MapAgentPrism prefix
        // (docs/hafiza/nswag-istemci-uretimi.md), which AgentPrismClientOptions.BaseAddress
        // normally carries; host.Client's own BaseAddress is bare, so it is set here instead.
        host.Client.BaseAddress = new Uri(host.Client.BaseAddress!, "agentprism/");

        return new AgentPrismApiClient(host.Client);
    }

    // The collections are no longer filled to work around a null default —
    // F-197 fixed that in nswag-postprocess-client.py's fifth pass, and
    // GeneratedClientCollectionDefaultTests is the test that proves a minimal
    // request works. Only Message is set here, which is also what a real
    // caller writes.
    // Fully qualified: this file also sees the server-side AgentPrism.AgentRunRequest
    // through the project's global usings, and the bare name is ambiguous (CS0104).
    private static AgentPrism.Client.Generated.AgentRunRequest RunRequest() => new()
    {
        Message = "merhaba",
    };
}
