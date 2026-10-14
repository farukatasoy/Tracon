using System.Text.Json;
using System.Text.Json.Nodes;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.OutputTruncationTests;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The two Anthropic stop reasons the SDK's adapter reports as an ordinary stop
/// reach the run as what they are (F-426): an answer cut off at the context window
/// is a cut-off answer, and a paused turn is continued until the model ends it.
/// </summary>
/// <remarks>
/// <para>
/// Measured (Anthropic SDK 12.39.0, the adapter decompiled): <c>ToFinishReason</c>
/// maps only <c>refusal</c>, <c>max_tokens</c> and <c>tool_use</c>; every other stop
/// reason became <c>Stop</c>. A structured answer cut off by the context window was
/// rejected as "invalid JSON", a plain one completed without a trace, and a paused
/// turn closed the run <c>Completed</c> with the half-done answer.
/// </para>
/// <para>
/// Through the whole host and the real SDK, against a local Messages API: only that
/// path shows what the adapter does with the stop reason and what the continuation
/// request carries.
/// </para>
/// </remarks>
public sealed class AnthropicStopReasonTests
{
    private const string PausedSearch = """
        {"id":"msg_paused","type":"message","role":"assistant","model":"claude-test",
         "content":[
           {"type":"server_tool_use","id":"srvtoolu_01","name":"web_search","input":{"query":"tracon release"}},
           {"type":"web_search_tool_result","tool_use_id":"srvtoolu_01","content":[
             {"type":"web_search_result","url":"https://example.com/notes","title":"Release notes","encrypted_content":"opaque-01","page_age":null}]},
           {"type":"text","text":"The search found the notes."}],
         "stop_reason":"pause_turn","stop_sequence":null,
         "usage":{"input_tokens":20,"output_tokens":7}}
        """;

    private const string FinishedAnswer = """
        {"id":"msg_final","type":"message","role":"assistant","model":"claude-test",
         "content":[{"type":"text","text":"The release ships on Friday."}],
         "stop_reason":"end_turn","stop_sequence":null,
         "usage":{"input_tokens":30,"output_tokens":9}}
        """;

    [Fact]
    public async Task A_structured_answer_cut_off_at_the_context_window_fails_as_truncated_and_names_the_window()
    {
        await using var anthropic = await FakeAnthropicServer.StartAsync();
        anthropic.Respond(Message("{\"answer\":", "model_context_window_exceeded"));

        await using var host = await StartAsync(anthropic, structured: true);

        using var response = await PostBufferedAsync(host);

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Failed);
        var error = run.Error.ShouldNotBeNull();
        error.Type.ShouldBe(TraconOutputTruncatedException.OutputTruncatedErrorType);
        error.Class.ShouldBe(RunErrorClass.OutputTruncated);
        error.Message.ShouldContain("context window");
        error.Message.ShouldNotContain("Raise ModelBinding.MaxOutputTokens", Case.Sensitive, "a larger output limit does not fit in a full window");
    }

    [Fact]
    public async Task A_plain_answer_cut_off_at_the_context_window_completes_with_a_truncation_event_that_names_the_window()
    {
        await using var anthropic = await FakeAnthropicServer.StartAsync();
        anthropic.Respond(Message("The answer stops in the mid", "model_context_window_exceeded"));

        await using var host = await StartAsync(anthropic, structured: false);

        using var response = await PostBufferedAsync(host);

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Completed);
        var truncated = (await EventsAsync(host, run.Id))
            .Where(static e => string.Equals(e.CustomType, RunEventCustomTypes.OutputTruncated, StringComparison.Ordinal))
            .ShouldHaveSingleItem();
        truncated.Text.ShouldNotBeNull().ShouldContain("context window");

        using var payload = JsonDocument.Parse(truncated.Payload!);
        payload.RootElement.GetProperty("cause").GetString().ShouldBe("context_window");
    }

    [Fact]
    public async Task A_paused_turn_is_sent_back_as_it_is_and_the_run_ends_with_the_finished_answer()
    {
        await using var anthropic = await FakeAnthropicServer.StartAsync();
        anthropic.Respond(PausedSearch).Respond(FinishedAnswer);

        await using var host = await StartAsync(anthropic, structured: false);

        using var response = await PostBufferedAsync(host);
        response.EnsureSuccessStatusCode();

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Completed);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .ShouldContain("The release ships on Friday.");
        anthropic.Bodies.Count.ShouldBe(2, "a paused turn closed the run with its half-done answer");

        // The continuation carries the paused turn's blocks as the API sent them,
        // the server tool's call and result included.
        var continued = anthropic.Bodies[1]["messages"]!.AsArray()[^1]!.AsObject();
        continued["role"]!.GetValue<string>().ShouldBe("assistant");
        var blocks = continued["content"]!.AsArray().Select(static block => block!["type"]!.GetValue<string>()).ToArray();
        blocks.ShouldBe(["server_tool_use", "web_search_tool_result", "text"]);
        continued["content"]![1]!["content"]![0]!["encrypted_content"]!.GetValue<string>().ShouldBe("opaque-01");

        // Both calls are paid for.
        run.Usage.ShouldNotBeNull().InputTokens.ShouldBe(50);
        run.Usage.OutputTokens.ShouldBe(16);
    }

    [Fact]
    public async Task A_paused_streamed_turn_is_continued_and_the_stream_ends_with_the_finished_answer()
    {
        await using var anthropic = await FakeAnthropicServer.StartAsync();
        anthropic
            .Stream(Events("msg_paused", "The search found the notes.", "pause_turn"))
            .Stream(Events("msg_final", "The release ships on Friday.", "end_turn"));

        await using var host = await StartAsync(anthropic, structured: false);

        var frames = await PostStreamingAsync(host);

        frames.ShouldNotContain(static frame => string.Equals(frame.Event, "error", StringComparison.Ordinal));
        string.Concat(frames.Select(static frame => frame.Data)).ShouldContain("Friday");
        anthropic.Bodies.Count.ShouldBe(2);

        var continued = anthropic.Bodies[1]["messages"]!.AsArray()[^1]!.AsObject();
        continued["role"]!.GetValue<string>().ShouldBe("assistant");
        continued["content"]![0]!["text"]!.GetValue<string>().ShouldBe("The search found the notes.");

        (await SingleRunAsync(host)).Status.ShouldBe(RunStatus.Completed);
    }

    /// <summary>
    /// A streamed paused turn whose server tool streamed its input is sent back whole:
    /// the call with its parsed input, the result, and the text.
    /// </summary>
    /// <remarks>
    /// The SDK's own aggregator throws on the first <c>input_json_delta</c> of a server
    /// tool block, so the client rebuilds the blocks itself. A text-only stream does not
    /// reach that path; this one does.
    /// </remarks>
    [Fact]
    public async Task A_streamed_paused_turn_with_a_server_tool_is_sent_back_with_the_call_its_input_and_its_result()
    {
        await using var anthropic = await FakeAnthropicServer.StartAsync();
        anthropic
            .Stream(PausedSearchEvents())
            .Stream(Events("msg_final", "The release ships on Friday.", "end_turn"));

        await using var host = await StartAsync(anthropic, structured: false);

        var frames = await PostStreamingAsync(host);

        frames.ShouldNotContain(static frame => string.Equals(frame.Event, "error", StringComparison.Ordinal));
        string.Concat(frames.Select(static frame => frame.Data)).ShouldContain("Friday");
        anthropic.Bodies.Count.ShouldBe(2);

        var continued = anthropic.Bodies[1]["messages"]!.AsArray()[^1]!.AsObject();
        continued["role"]!.GetValue<string>().ShouldBe("assistant");
        var blocks = continued["content"]!.AsArray();
        blocks.Select(static block => block!["type"]!.GetValue<string>()).ToArray()
            .ShouldBe(["server_tool_use", "web_search_tool_result", "text"]);
        blocks[0]!["input"]!["query"]!.GetValue<string>().ShouldBe("tracon release");
        blocks[1]!["content"]![0]!["encrypted_content"]!.GetValue<string>().ShouldBe("opaque-01");
        blocks[2]!["text"]!.GetValue<string>().ShouldBe("The search found the notes.");

        (await SingleRunAsync(host)).Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task A_streamed_answer_cut_off_at_the_context_window_records_a_truncation_that_names_the_window()
    {
        await using var anthropic = await FakeAnthropicServer.StartAsync();
        anthropic.Stream(Events("msg_cut", "The answer stops in the mid", "model_context_window_exceeded"));

        await using var host = await StartAsync(anthropic, structured: false);

        await PostStreamingAsync(host);

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Completed);
        var truncated = (await EventsAsync(host, run.Id))
            .Where(static e => string.Equals(e.CustomType, RunEventCustomTypes.OutputTruncated, StringComparison.Ordinal))
            .ShouldHaveSingleItem();
        truncated.Text.ShouldNotBeNull().ShouldContain("context window");

        using var payload = JsonDocument.Parse(truncated.Payload!);
        payload.RootElement.GetProperty("cause").GetString().ShouldBe("context_window");
    }

    [Fact]
    public async Task A_turn_that_keeps_pausing_fails_the_run_after_a_bounded_number_of_continuations()
    {
        await using var anthropic = await FakeAnthropicServer.StartAsync();

        for (var answer = 0; answer < 10; answer++)
        {
            anthropic.Respond(PausedSearch);
        }

        await using var host = await StartAsync(anthropic, structured: false);

        using var response = await PostBufferedAsync(host);

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Failed, "a turn that never ends must not complete the run");
        run.Error.ShouldNotBeNull().Message.ShouldContain("AnthropicTurnPausedException");
        anthropic.Bodies.Count.ShouldBe(6, "the first call and five continuations");
    }

    private static string Message(string text, string stopReason)
        => new JsonObject
        {
            ["id"] = "msg_cut",
            ["type"] = "message",
            ["role"] = "assistant",
            ["model"] = "claude-test",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["stop_reason"] = stopReason,
            ["stop_sequence"] = null,
            ["usage"] = new JsonObject { ["input_tokens"] = 40, ["output_tokens"] = 60 },
        }.ToJsonString();

    private static string Events(string id, string text, string stopReason)
    {
        var half = text.Length / 2;

        return string.Concat(
            Frame("message_start", MessageStart(id)),
            Frame("content_block_start", BlockStart(0, new JsonObject { ["type"] = "text", ["text"] = string.Empty })),
            Frame("content_block_delta", BlockDelta(0, new JsonObject { ["type"] = "text_delta", ["text"] = text[..half] })),
            Frame("content_block_delta", BlockDelta(0, new JsonObject { ["type"] = "text_delta", ["text"] = text[half..] })),
            Frame("content_block_stop", BlockStop(0)),
            MessageEnd(stopReason));
    }

    /// <summary>
    /// A paused search turn as the API streams it: the server tool's call with its
    /// input in two partial JSON deltas, its result whole in the start event, then text.
    /// </summary>
    private static string PausedSearchEvents()
    {
        var call = new JsonObject
        {
            ["type"] = "server_tool_use",
            ["id"] = "srvtoolu_01",
            ["name"] = "web_search",
            ["input"] = new JsonObject(),
        };

        var result = JsonNode.Parse("""
            {"type":"web_search_tool_result","tool_use_id":"srvtoolu_01","content":[
              {"type":"web_search_result","url":"https://example.com/notes","title":"Release notes","encrypted_content":"opaque-01","page_age":null}]}
            """)!.AsObject();

        return string.Concat(
            Frame("message_start", MessageStart("msg_paused")),
            Frame("content_block_start", BlockStart(0, call)),
            Frame("content_block_delta", BlockDelta(0, new JsonObject { ["type"] = "input_json_delta", ["partial_json"] = "{\"query\":" })),
            Frame("content_block_delta", BlockDelta(0, new JsonObject { ["type"] = "input_json_delta", ["partial_json"] = "\"tracon release\"}" })),
            Frame("content_block_stop", BlockStop(0)),
            Frame("content_block_start", BlockStart(1, result)),
            Frame("content_block_stop", BlockStop(1)),
            Frame("content_block_start", BlockStart(2, new JsonObject { ["type"] = "text", ["text"] = string.Empty })),
            Frame("content_block_delta", BlockDelta(2, new JsonObject { ["type"] = "text_delta", ["text"] = "The search found the notes." })),
            Frame("content_block_stop", BlockStop(2)),
            MessageEnd("pause_turn"));
    }

    private static JsonObject MessageStart(string id)
        => new()
        {
            ["type"] = "message_start",
            ["message"] = new JsonObject
            {
                ["id"] = id,
                ["type"] = "message",
                ["role"] = "assistant",
                ["model"] = "claude-test",
                ["content"] = new JsonArray(),
                ["stop_reason"] = null,
                ["stop_sequence"] = null,
                ["usage"] = new JsonObject { ["input_tokens"] = 20, ["output_tokens"] = 1 },
            },
        };

    private static JsonObject BlockStart(int index, JsonObject block)
        => new() { ["type"] = "content_block_start", ["index"] = index, ["content_block"] = block };

    private static JsonObject BlockDelta(int index, JsonObject delta)
        => new() { ["type"] = "content_block_delta", ["index"] = index, ["delta"] = delta };

    private static JsonObject BlockStop(int index)
        => new() { ["type"] = "content_block_stop", ["index"] = index };

    private static string MessageEnd(string stopReason)
        => string.Concat(
            Frame("message_delta", new JsonObject
            {
                ["type"] = "message_delta",
                ["delta"] = new JsonObject { ["stop_reason"] = stopReason, ["stop_sequence"] = null },
                ["usage"] = new JsonObject { ["output_tokens"] = 8 },
            }),
            Frame("message_stop", new JsonObject { ["type"] = "message_stop" }));

    private static string Frame(string name, JsonObject data) => $"event: {name}\ndata: {data.ToJsonString()}\n\n";

    private static Task<TraconTestHost> StartAsync(FakeAnthropicServer anthropic, bool structured)
        => TraconTestHost.StartAsync(configureTracon: builder => builder
            .UseAnthropic("test-anthropic-stop-reasons", options =>
            {
                options.Endpoint = anthropic.BaseAddress;
                options.MaxRetries = 0;
                options.Models.Add(new ModelDescriptor { Name = "claude-test", SupportsStructuredOutput = true });
            })
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Reply briefly.",
                Model = new ModelBinding
                {
                    Provider = AnthropicProviderNames.Anthropic,
                    Model = "claude-test",
                    MaxOutputTokens = 1_000,
                    ResponseFormat = structured
                        ? new AgentResponseFormat
                        {
                            Kind = AgentResponseFormatKind.JsonSchema,
                            Schema = JsonDocument.Parse(
                                """{"type":"object","properties":{"answer":{"type":"integer"}},"required":["answer"]}""").RootElement,
                            SchemaName = "answer",
                        }
                        : null,
                },
                Origin = AgentDefinitionOrigin.Code,
            }));
}
