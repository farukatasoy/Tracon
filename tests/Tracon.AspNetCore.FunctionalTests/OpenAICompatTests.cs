using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Verifies the wire format of the OpenAI-compatible endpoints and the agent
/// selection contract.
/// </summary>
public sealed class OpenAICompatTests
{
    private static readonly Uri Responses = new("/tracon/v1/responses", UriKind.Relative);
    private static readonly Uri ChatCompletions = new("/tracon/v1/chat/completions", UriKind.Relative);

    // --- /v1/responses ---

    [Fact]
    public async Task Responses_selects_the_agent_from_the_model_field()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = "kod-agent", input = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await TraconTestHost.ReadJsonAsync(response);

        json.GetProperty("object").GetString().ShouldBe("response");
        json.GetProperty("status").GetString().ShouldBe("completed");
        json.GetProperty("id").GetString().ShouldStartWith("resp_");
        json.GetProperty("output")[0].GetProperty("content")[0].GetProperty("text").GetString()
            .ShouldBe("Echo: hello");
    }

    [Fact]
    public async Task Responses_also_selects_the_agent_via_metadata_entity_id()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = "real-model-name", metadata = new { entity_id = "kod-agent" }, input = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Responses_lists_registered_agents_when_none_is_selected()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(Responses, new { input = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var message = (await TraconTestHost.ReadJsonAsync(response))
            .GetProperty("error").GetProperty("message").GetString();

        message.ShouldNotBeNull();
        message.ShouldContain("kod-agent");
    }

    [Fact]
    public async Task Responses_returns_an_OpenAI_shaped_error_for_a_nonexistent_agent()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = "no-such-agent", input = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // OpenAI SDKs parse the error in the {"error":{"message":...}} shape;
        // returning ProblemDetails would produce a meaningless error on the client.
        var json = await TraconTestHost.ReadJsonAsync(response);
        json.GetProperty("error").GetProperty("type").GetString().ShouldBe("model_not_found");
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
    }

    [Fact]
    public async Task Responses_streaming_uses_OpenAI_event_names()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = "kod-agent", input = "hello", stream = true });

        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());
        var names = frames.Select(static frame => frame.Event).ToList();

        names.ShouldContain(static name => string.Equals(name, "response.created", StringComparison.Ordinal));
        names.ShouldContain(static name => string.Equals(name, "response.output_text.delta", StringComparison.Ordinal));
        names.ShouldContain(static name => string.Equals(name, "response.completed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Responses_chains_history_via_previous_response_id()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        string firstResponseId;

        using (var first = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = "kod-agent", input = "first turn" }))
        {
            first.EnsureSuccessStatusCode();
            firstResponseId = (await TraconTestHost.ReadJsonAsync(first)).GetProperty("id").GetString()!;
        }

        using var second = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = "kod-agent", input = "second turn", previous_response_id = firstResponseId });

        second.EnsureSuccessStatusCode();

        // The session was stored under the first response id; the second call must have found it.
        using var sessions = await host.Client.GetAsync(new Uri("/tracon/api/sessions", UriKind.Relative));
        var ids = (await TraconTestHost.ReadJsonAsync(sessions))
            .EnumerateArray()
            .Select(static session => session.GetProperty("id").GetString())
            .ToList();

        ids.ShouldContain(id => string.Equals(id, firstResponseId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Responses_second_turn_loads_the_session_the_first_turn_saved()
    {
        // The load and the save go through the Agent Framework's
        // session store key. The chaining test above only shows that the first
        // record EXISTS; a load that missed it would still pass. The stored
        // state of the second record must carry BOTH turns, the first record
        // only its own.
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        string firstResponseId;
        string secondResponseId;

        using (var first = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = "kod-agent", input = "turn-one-marker" }))
        {
            first.EnsureSuccessStatusCode();
            firstResponseId = (await TraconTestHost.ReadJsonAsync(first)).GetProperty("id").GetString()!;
        }

        using (var second = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = "kod-agent", input = "turn-two-marker", previous_response_id = firstResponseId }))
        {
            second.EnsureSuccessStatusCode();
            secondResponseId = (await TraconTestHost.ReadJsonAsync(second)).GetProperty("id").GetString()!;
        }

        var firstState = await StoredStateAsync(host, firstResponseId);
        var secondState = await StoredStateAsync(host, secondResponseId);

        firstState.ShouldContain("turn-one-marker");
        firstState.ShouldNotContain("turn-two-marker", Case.Sensitive);
        secondState.ShouldContain("turn-one-marker");
        secondState.ShouldContain("turn-two-marker");
    }

    [Fact]
    public async Task Responses_streaming_saves_the_session_under_the_conversation_id()
    {
        // The streamed turn saves its session after the last frame;
        // the other streaming test only reads event names.
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using (var response = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = "kod-agent", input = "streamed-marker", stream = true, conversation = "conv-streamed" }))
        {
            response.EnsureSuccessStatusCode();
            _ = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());
        }

        (await StoredStateAsync(host, "conv-streamed")).ShouldContain("streamed-marker");
    }

    private static async Task<string> StoredStateAsync(TraconTestHost host, string sessionId)
    {
        using var response = await host.Client.GetAsync(
            new Uri($"/tracon/api/sessions/{sessionId}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"session '{sessionId}' was not saved");

        return (await TraconTestHost.ReadJsonAsync(response)).GetProperty("state").GetRawText();
    }

    [Fact]
    public async Task Responses_conversation_id_stays_stable_across_turns()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        for (var turn = 0; turn < 2; turn++)
        {
            using var response = await host.Client.PostAsJsonAsync(
                Responses,
                new { model = "kod-agent", input = $"turn {turn}", conversation = "conv-fixed" });

            response.EnsureSuccessStatusCode();
            (await TraconTestHost.ReadJsonAsync(response))
                .GetProperty("conversation").GetProperty("id").GetString().ShouldBe("conv-fixed");
        }

        using var sessions = await host.Client.GetAsync(new Uri("/tracon/api/sessions", UriKind.Relative));
        var json = await TraconTestHost.ReadJsonAsync(sessions);

        // Two turns must produce a single session.
        json.EnumerateArray()
            .Count(static session => string.Equals(session.GetProperty("id").GetString(), "conv-fixed", StringComparison.Ordinal))
            .ShouldBe(1);
    }

    /// <summary>
    /// <c>OpenAIResponses.WriteResponse</c> (MAF)
    /// does not recognize a <c>ToolApprovalRequestContent</c> and SILENTLY
    /// drops it from 'output'; all the caller saw was an empty array and
    /// <c>status: "completed"</c> — the pending-approval call was never visible.
    /// </summary>
    [Fact]
    public async Task Responses_shows_a_pending_approval_tool_call_in_output()
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder
            .AddModelProvider(new FakeModelProvider("approval-model")
                .CallsTool("cancel_order", new { orderId = "ORD-1" })
                .EchoesLastToolResult())
            .AddTool(
                (Func<string, string>)(orderId => $"{orderId} canceled."),
                name: "cancel_order",
                description: "Cancels an order.",
                configure: options => options.RequiresApproval = true)
            .AddAgent(new AgentDefinition
            {
                Name = "approval-agent",
                Instructions = "Reply briefly.",
                Model = new ModelBinding { Provider = "approval-model", Model = "approval-1" },
                ToolNames = ["cancel_order"],
            }));

        using var response = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = "approval-agent", input = "cancel my order ORD-1" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var output = (await TraconTestHost.ReadJsonAsync(response))
            .GetProperty("output").EnumerateArray().ToList();

        var call = output.ShouldHaveSingleItem();
        call.GetProperty("type").GetString().ShouldBe("function_call");
        call.GetProperty("name").GetString().ShouldBe("cancel_order");
    }

    [Fact]
    public async Task Responses_converts_an_embedded_data_uri_to_an_attachment_and_delivers_it_resolved_to_the_model()
    {
        // '/v1/responses' accepts
        // OpenAI-shaped image input. MAF's own body parser converts the 'data:'
        // URI into DataContent; before sending it to the agent, Tracon
        // takes an attachment and converts it into a UriContent reference (to
        // keep the message small), then resolves it again right before the
        // model call.
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var png = Png();
        var dataUri = $"data:image/png;base64,{Convert.ToBase64String(png)}";

        using var response = await host.Client.PostAsJsonAsync(
            Responses,
            new
            {
                model = "kod-agent",
                input = new object[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "input_text", text = "describe this image" },
                            new { type = "input_image", image_url = dataUri },
                        },
                    },
                },
            });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var attachments = await host.Services.GetRequiredService<IAttachmentStore>()
            .ListAsync(new AttachmentQuery { TenantId = "default" });

        attachments.ShouldHaveSingleItem().MediaType.ShouldBe("image/png");

        var echo = host.Services.GetServices<IModelProvider>().OfType<FakeModelProvider>().Single();

        var content = echo.Requests[^1].Messages
            .SelectMany(static message => message.Contents)
            .OfType<DataContent>()
            .ShouldHaveSingleItem();

        content.Data.ToArray().ShouldBe(png);
    }

    private static byte[] Png()
    {
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        return [.. signature, .. new byte[8]];
    }

    // --- /v1/chat/completions ---

    [Fact]
    public async Task ChatCompletions_returns_an_OpenAI_shaped_response()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(
            ChatCompletions,
            new
            {
                model = "kod-agent",
                messages = new[] { new { role = "user", content = "hello" } },
            });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await TraconTestHost.ReadJsonAsync(response);

        json.GetProperty("object").GetString().ShouldBe("chat.completion");
        json.GetProperty("id").GetString().ShouldStartWith("chatcmpl-");
        json.GetProperty("model").GetString().ShouldBe("kod-agent");

        var choice = json.GetProperty("choices")[0];
        choice.GetProperty("finish_reason").GetString().ShouldBe("stop");
        choice.GetProperty("message").GetProperty("role").GetString().ShouldBe("assistant");
        choice.GetProperty("message").GetProperty("content").GetString().ShouldBe("Echo: hello");
    }

    [Fact]
    public async Task ChatCompletions_resolves_multipart_content()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(
            ChatCompletions,
            new
            {
                model = "kod-agent",
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new[] { new { type = "text", text = "multipart message" } },
                    },
                },
            });

        response.EnsureSuccessStatusCode();

        (await TraconTestHost.ReadJsonAsync(response))
            .GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
            .ShouldBe("Echo: multipart message");
    }

    [Fact]
    public async Task ChatCompletions_streaming_ends_with_the_DONE_marker()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(
            ChatCompletions,
            new
            {
                model = "kod-agent",
                stream = true,
                messages = new[] { new { role = "user", content = "hello" } },
            });

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        frames[^1].Data.ShouldBe("[DONE]");
        frames[0].Data.ShouldContain("chat.completion.chunk");
        frames.Select(static frame => frame.Data)
            .ShouldContain(static data => data.Contains("Echo: hello", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChatCompletions_is_stateless_and_does_not_open_a_session()
    {
        // In the Chat Completions contract, the client carries the history. If
        // a session were opened, the history would be managed twice and
        // messages would appear duplicated.
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using (var response = await host.Client.PostAsJsonAsync(
            ChatCompletions,
            new
            {
                model = "kod-agent",
                messages = new[] { new { role = "user", content = "hello" } },
            }))
        {
            response.EnsureSuccessStatusCode();
        }

        using var sessions = await host.Client.GetAsync(new Uri("/tracon/api/sessions", UriKind.Relative));

        (await TraconTestHost.ReadJsonAsync(sessions)).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task ChatCompletions_rejects_a_request_with_no_messages()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(
            ChatCompletions,
            new { model = "kod-agent" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await TraconTestHost.ReadJsonAsync(response))
            .GetProperty("error").GetProperty("type").GetString().ShouldBe("invalid_request_error");
    }

    [Theory]
    [InlineData("length", false, "length")]
    [InlineData("length", true, "length")]
    [InlineData("context_window_exceeded", false, "length")]
    [InlineData("context_window_exceeded", true, "length")]
    [InlineData("content_filter", false, "content_filter")]
    [InlineData("content_filter", true, "content_filter")]
    [InlineData("stop", false, "stop")]
    [InlineData("stop", true, "stop")]
    public async Task ChatCompletions_reports_the_final_finish_reason(string modelReason, bool stream, string expected)
    {
        // The field said "stop" for every answer, so an OpenAI SDK
        // caller could not tell a cut-off answer from a complete one.
        var provider = new FinishReasonModelProvider()
            .Text("Part of an answer", new ChatFinishReason(modelReason), inputTokens: 30, outputTokens: 50);

        await using var host = await OutputTruncationTests.StartAsync(provider, OutputTruncationTests.PlainText(maxOutputTokens: 50));

        (await ChatCompletionFinishReasonAsync(host, stream)).ShouldBe(expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChatCompletions_reports_length_for_a_cut_harness_loop_answer(bool stream)
    {
        // A harness loop agent answers through MAF's harness and loop
        // decorators, which rebuild the response. The cut final iteration must still
        // reach the caller as "length".
        var provider = new FinishReasonModelProvider()
            .Text("ALL DONE, and the reason is that the explanation is cu", ChatFinishReason.Length, inputTokens: 30, outputTokens: 50);

        await using var host = await OutputTruncationTests.StartAsync(provider, HarnessLoopAgent());

        (await ChatCompletionFinishReasonAsync(host, stream)).ShouldBe("length");
        provider.Calls.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChatCompletions_reports_the_last_harness_iteration_not_an_earlier_cut_one(bool stream)
    {
        // The first iteration was cut and the loop ran again; the answer the caller
        // gets is the last iteration's, which completed.
        var provider = new FinishReasonModelProvider()
            .Text("still working", ChatFinishReason.Length, inputTokens: 10, outputTokens: 50)
            .Text("ALL DONE.", ChatFinishReason.Stop, inputTokens: 10, outputTokens: 5);

        await using var host = await OutputTruncationTests.StartAsync(provider, HarnessLoopAgent());

        (await ChatCompletionFinishReasonAsync(host, stream)).ShouldBe("stop");
        provider.Calls.ShouldBe(2);
    }

    private static AgentDefinition HarnessLoopAgent()
        => OutputTruncationTests.PlainText(maxOutputTokens: 50) with
        {
            Harness = new HarnessSettings
            {
                Loop = new LoopSettings
                {
                    Criteria = [new LoopCriterion { Kind = "completionMarker", Marker = "ALL DONE" }],
                    MaxIterations = 4,
                },
            },
        };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChatCompletions_reports_stop_for_a_complete_answer_after_a_tool_turn(bool stream)
    {
        // The tools run on the server; the caller never sees the tool turn, so its
        // reason is not the answer's. The tool turn stopped at the limit here: a
        // mapping that looked at ANY reason instead of the last one says "length".
        var provider = new FinishReasonModelProvider()
            .ToolCall("lookup_fact", ChatFinishReason.Length, inputTokens: 20, outputTokens: 10)
            .Text("The answer is 42.", ChatFinishReason.Stop, inputTokens: 30, outputTokens: 10);

        await using var host = await OutputTruncationTests.StartAsync(
            provider,
            OutputTruncationTests.PlainText(maxOutputTokens: 50, toolNames: ["lookup_fact"]),
            configureTracon: static builder => builder.AddTool(
                (Func<string>)(static () => "42"), name: "lookup_fact", description: "Returns a fact."));

        (await ChatCompletionFinishReasonAsync(host, stream)).ShouldBe("stop");
        provider.Calls.ShouldBe(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChatCompletions_reports_length_with_run_recording_off(bool stream)
    {
        // The mapping reads MAF's own finish reason, not the run record.
        var provider = new FinishReasonModelProvider()
            .Text("Part of an answer", ChatFinishReason.Length, inputTokens: 30, outputTokens: 50);

        await using var host = await OutputTruncationTests.StartAsync(
            provider,
            OutputTruncationTests.PlainText(maxOutputTokens: 50),
            configureServices: static services => services.Configure<TraconOptions>(
                static options => options.RunRecording.Enabled = false));

        (await ChatCompletionFinishReasonAsync(host, stream)).ShouldBe("length");
    }

    /// <summary>
    /// Calls <c>/v1/chat/completions</c> for the truncation test agent and returns the
    /// answer's <c>finish_reason</c>. On the stream it also checks the shape: only the
    /// last chunk carries the field, and <c>[DONE]</c> is the last line.
    /// </summary>
    private static async Task<string?> ChatCompletionFinishReasonAsync(TraconTestHost host, bool stream)
    {
        using var response = await host.Client.PostAsJsonAsync(
            ChatCompletions,
            new
            {
                model = OutputTruncationTests.AgentName,
                stream,
                messages = new[] { new { role = "user", content = "hello" } },
            });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        if (!stream)
        {
            return (await TraconTestHost.ReadJsonAsync(response))
                .GetProperty("choices")[0].GetProperty("finish_reason").GetString();
        }

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        frames[^1].Data.ShouldBe("[DONE]");

        var reasons = new List<string?>();

        foreach (var frame in frames.Take(frames.Count - 1))
        {
            using var chunk = JsonDocument.Parse(frame.Data);
            var choice = chunk.RootElement.GetProperty("choices")[0];

            reasons.Add(choice.TryGetProperty("finish_reason", out var reason) ? reason.GetString() : null);
        }

        reasons.Take(reasons.Count - 1).ShouldAllBe(static reason => reason == null);

        return reasons[^1];
    }

    [Fact]
    public async Task ChatCompletions_returns_usage_information_in_snake_case()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(
            ChatCompletions,
            new
            {
                model = "kod-agent",
                messages = new[] { new { role = "user", content = "hello" } },
            });

        var json = await TraconTestHost.ReadJsonAsync(response);

        // The echo provider does not report usage; the field is omitted as
        // null. What matters is that the field NAMES follow the OpenAI shape.
        if (json.TryGetProperty("usage", out var usage) && usage.ValueKind is not JsonValueKind.Null)
        {
            usage.TryGetProperty("prompt_tokens", out _).ShouldBeTrue();
            usage.TryGetProperty("completion_tokens", out _).ShouldBeTrue();
            usage.TryGetProperty("total_tokens", out _).ShouldBeTrue();
        }
    }
}
