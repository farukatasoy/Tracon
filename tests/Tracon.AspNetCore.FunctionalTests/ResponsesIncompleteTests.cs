using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// F-503: <c>POST /v1/responses</c> says an answer is incomplete the way OpenAI's own Responses API
/// does. The body of the answer comes from Microsoft Agent Framework, which never reads the finish
/// reason (measured: every reason ends in <c>response.completed</c> with <c>status: "completed"</c>),
/// so the status, the terminal stream event and the message item's status are only real over HTTP.
/// </summary>
/// <remarks>
/// The pure rewrite rules (a body MAF already marked, a frame that is not the terminal one) are in
/// <see cref="ResponsesIncompletePatchTests"/>.
/// </remarks>
public sealed class ResponsesIncompleteTests
{
    private static readonly Uri Responses = new("/tracon/v1/responses", UriKind.Relative);

    [Theory]
    [InlineData("length", false)]
    [InlineData("length", true)]
    [InlineData("context_window_exceeded", false)]
    [InlineData("context_window_exceeded", true)]
    public async Task A_cut_off_answer_is_incomplete_because_of_max_output_tokens(string modelReason, bool stream)
    {
        var provider = new FinishReasonModelProvider()
            .Text("Part of an answer", new ChatFinishReason(modelReason), inputTokens: 30, outputTokens: 50);

        await using var host = await OutputTruncationTests.StartAsync(provider, OutputTruncationTests.PlainText(maxOutputTokens: 50));

        var answer = await AskAsync(host, stream);

        answer.Status.ShouldBe("incomplete");
        answer.IncompleteReason.ShouldBe("max_output_tokens");
        answer.MessageStatuses.ShouldBe(["incomplete"]);
        answer.Text.ShouldBe("Part of an answer");

        if (stream)
        {
            answer.TerminalEvents.ShouldBe(["response.incomplete"]);
            answer.MessageItemDoneStatuses.ShouldBe(["incomplete"]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_content_filtered_answer_with_text_is_incomplete_because_of_content_filter(bool stream)
    {
        var provider = new FinishReasonModelProvider()
            .Text("Part of an answer", ChatFinishReason.ContentFilter, inputTokens: 30, outputTokens: 50);

        await using var host = await OutputTruncationTests.StartAsync(provider, OutputTruncationTests.PlainText(maxOutputTokens: 50));

        var answer = await AskAsync(host, stream);

        answer.Status.ShouldBe("incomplete");
        answer.IncompleteReason.ShouldBe("content_filter");
        answer.MessageStatuses.ShouldBe(["incomplete"]);

        if (stream)
        {
            answer.TerminalEvents.ShouldBe(["response.incomplete"]);
            answer.MessageItemDoneStatuses.ShouldBe(["incomplete"]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_complete_answer_stays_completed(bool stream)
    {
        var provider = new FinishReasonModelProvider()
            .Text("A full answer.", ChatFinishReason.Stop, inputTokens: 30, outputTokens: 5);

        await using var host = await OutputTruncationTests.StartAsync(provider, OutputTruncationTests.PlainText(maxOutputTokens: 50));

        var answer = await AskAsync(host, stream);

        answer.Status.ShouldBe("completed");
        answer.HasIncompleteDetails.ShouldBeFalse();
        answer.MessageStatuses.ShouldBe(["completed"]);

        if (stream)
        {
            answer.TerminalEvents.ShouldBe(["response.completed"]);
            answer.MessageItemDoneStatuses.ShouldBe(["completed"]);
        }
    }

    [Fact]
    public async Task An_incomplete_stream_differs_from_a_complete_one_only_in_its_terminal_event_and_item_status()
    {
        // Only the tail changes; every frame before it is what MAF wrote, in the same order, and the
        // numbering a client reads stays gap-free.
        var complete = await StreamOf(ChatFinishReason.Stop);
        var cut = await StreamOf(ChatFinishReason.Length);

        cut.Events.SkipLast(1).ShouldBe(complete.Events.SkipLast(1));
        complete.Events[^1].ShouldBe("response.completed");
        cut.Events[^1].ShouldBe("response.incomplete");
        complete.MessageItemDoneStatuses.ShouldBe(["completed"]);
        cut.MessageItemDoneStatuses.ShouldBe(["incomplete"]);

        cut.SequenceNumbers.ShouldBe([.. Enumerable.Range(0, cut.Events.Count)]);
        complete.SequenceNumbers.ShouldBe([.. Enumerable.Range(0, complete.Events.Count)]);

        static async Task<ResponsesAnswer> StreamOf(ChatFinishReason reason)
        {
            var provider = new FinishReasonModelProvider()
                .Text("Part of an answer", reason, inputTokens: 30, outputTokens: 50);

            await using var host = await OutputTruncationTests.StartAsync(provider, OutputTruncationTests.PlainText(maxOutputTokens: 50));

            return await AskAsync(host, stream: true);
        }
    }

    [Theory]
    [InlineData("length", false)]
    [InlineData("length", true)]
    [InlineData("content_filter", false)]
    [InlineData("content_filter", true)]
    public async Task A_tool_turn_cut_at_the_limit_or_filtered_does_not_make_the_final_answer_incomplete(string toolTurnReason, bool stream)
    {
        // The tools run on the server; the caller never sees the tool turn, so its reason is not the answer's.
        // The rule holds for a filter exactly as for the output limit: only the turn that ends the answer decides.
        var provider = new FinishReasonModelProvider()
            .ToolCall("lookup_fact", new ChatFinishReason(toolTurnReason), inputTokens: 20, outputTokens: 10)
            .Text("The answer is 42.", ChatFinishReason.Stop, inputTokens: 30, outputTokens: 10);

        await using var host = await OutputTruncationTests.StartAsync(
            provider,
            OutputTruncationTests.PlainText(maxOutputTokens: 50, toolNames: ["lookup_fact"]),
            configureTracon: static builder => builder.AddTool(
                (Func<string>)(static () => "42"), name: "lookup_fact", description: "Returns a fact."));

        var answer = await AskAsync(host, stream);

        answer.Status.ShouldBe("completed");
        answer.HasIncompleteDetails.ShouldBeFalse();
        answer.MessageStatuses.ShouldAllBe(static status => status == "completed");
        provider.Calls.ShouldBe(2);

        if (stream)
        {
            answer.TerminalEvents.ShouldBe(["response.completed"]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_final_answer_cut_after_a_tool_turn_is_incomplete(bool stream)
    {
        // The other direction of the rule above: the tool turn completed, the answer that follows it was cut off.
        var provider = new FinishReasonModelProvider()
            .ToolCall("lookup_fact", ChatFinishReason.ToolCalls, inputTokens: 20, outputTokens: 10)
            .Text("The answer is 4", ChatFinishReason.Length, inputTokens: 30, outputTokens: 50);

        await using var host = await OutputTruncationTests.StartAsync(
            provider,
            OutputTruncationTests.PlainText(maxOutputTokens: 50, toolNames: ["lookup_fact"]),
            configureTracon: static builder => builder.AddTool(
                (Func<string>)(static () => "42"), name: "lookup_fact", description: "Returns a fact."));

        var answer = await AskAsync(host, stream);

        answer.Status.ShouldBe("incomplete");
        answer.IncompleteReason.ShouldBe("max_output_tokens");
        answer.MessageStatuses.ShouldBe(["incomplete"]);
        provider.Calls.ShouldBe(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_cut_final_harness_iteration_is_incomplete(bool stream)
    {
        var provider = new FinishReasonModelProvider()
            .Text("ALL DONE, and the reason is that the explanation is cu", ChatFinishReason.Length, inputTokens: 30, outputTokens: 50);

        await using var host = await OutputTruncationTests.StartAsync(provider, HarnessLoopAgent());

        var answer = await AskAsync(host, stream);

        answer.Status.ShouldBe("incomplete");
        answer.IncompleteReason.ShouldBe("max_output_tokens");
        provider.Calls.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_earlier_cut_harness_iteration_does_not_make_the_last_one_incomplete(bool stream)
    {
        // The loop hands over its last iteration only. The first one was cut and the loop ran again.
        var provider = new FinishReasonModelProvider()
            .Text("still working", ChatFinishReason.Length, inputTokens: 10, outputTokens: 50)
            .Text("ALL DONE.", ChatFinishReason.Stop, inputTokens: 10, outputTokens: 5);

        await using var host = await OutputTruncationTests.StartAsync(provider, HarnessLoopAgent());

        var answer = await AskAsync(host, stream);

        answer.Status.ShouldBe("completed");
        answer.HasIncompleteDetails.ShouldBeFalse();
        answer.Text.ShouldBe("ALL DONE.");
        provider.Calls.ShouldBe(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_status_does_not_depend_on_run_recording(bool stream)
    {
        // The status reads MAF's own finish reason, not the run record.
        var provider = new FinishReasonModelProvider()
            .Text("Part of an answer", ChatFinishReason.Length, inputTokens: 30, outputTokens: 50);

        await using var host = await OutputTruncationTests.StartAsync(
            provider,
            OutputTruncationTests.PlainText(maxOutputTokens: 50),
            configureServices: static services => services.Configure<TraconOptions>(
                static options => options.RunRecording.Enabled = false));

        var answer = await AskAsync(host, stream);

        answer.Status.ShouldBe("incomplete");
        answer.IncompleteReason.ShouldBe("max_output_tokens");
    }

    [Theory]
    [InlineData("length", "tracon.output.truncated", false)]
    [InlineData("length", "tracon.output.truncated", true)]
    [InlineData("content_filter", "tracon.output.filtered", false)]
    [InlineData("content_filter", "tracon.output.filtered", true)]
    public async Task The_run_behind_an_incomplete_answer_still_records_its_notice(string modelReason, string customType, bool stream)
    {
        // The stream passes through the rewriter's layer before MAF reads it; the detector's notice is written
        // from inside the model call, so it must still reach the run on this surface.
        var provider = new FinishReasonModelProvider()
            .Text("Part of an answer", new ChatFinishReason(modelReason), inputTokens: 30, outputTokens: 50);

        await using var host = await OutputTruncationTests.StartAsync(provider, OutputTruncationTests.PlainText(maxOutputTokens: 50));

        await AskAsync(host, stream);

        var run = await OutputTruncationTests.SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Completed);
        (await OutputTruncationTests.EventsAsync(host, run.Id))
            .Where(e => string.Equals(e.CustomType, customType, StringComparison.Ordinal))
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_message_closed_before_the_stream_ends_keeps_its_own_status_but_the_final_response_is_incomplete()
    {
        // MAF closes a message item as soon as another kind of item follows its text (measured with a reasoning part),
        // which is before the finish reason is known. The terminal event, with the whole output, is the source of truth.
        await using var host = await TraconTestHost.StartAsync(static builder => builder
            .AddModelProvider(new ReasoningAfterTextModelProvider())
            .AddAgent(new AgentDefinition
            {
                Name = OutputTruncationTests.AgentName,
                DisplayName = "Truncation Agent",
                Description = "An agent used by the Responses incomplete tests.",
                Instructions = "Reply briefly.",
                Model = new ModelBinding
                {
                    Provider = ReasoningAfterTextModelProvider.ProviderName,
                    Model = ReasoningAfterTextModelProvider.ModelName,
                    MaxOutputTokens = 50,
                },
                Origin = AgentDefinitionOrigin.Code,
            }));

        var answer = await AskAsync(host, stream: true);

        answer.Status.ShouldBe("incomplete");
        answer.IncompleteReason.ShouldBe("max_output_tokens");
        answer.TerminalEvents.ShouldBe(["response.incomplete"]);
        answer.MessageStatuses.ShouldBe(["incomplete"]);
        answer.MessageItemDoneStatuses.ShouldBe(["completed"]);
    }

    [Fact]
    public async Task A_pending_approval_response_keeps_its_status_and_gets_its_function_call_item()
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
            new { model = "approval-agent", input = "cancel my order ORD-1" },
            TestContext.Current.CancellationToken);

        var json = await TraconTestHost.ReadJsonAsync(response);

        json.GetProperty("status").GetString().ShouldBe("completed");
        json.TryGetProperty("incomplete_details", out _).ShouldBeFalse();
        json.GetProperty("output").EnumerateArray().ShouldHaveSingleItem()
            .GetProperty("type").GetString().ShouldBe("function_call");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_empty_content_filtered_answer_is_still_an_error_not_an_incomplete_response(bool stream)
    {
        var provider = new FinishReasonModelProvider()
            .Text(string.Empty, ChatFinishReason.ContentFilter, inputTokens: 900, outputTokens: 0);

        await using var host = await OutputTruncationTests.StartAsync(provider, OutputTruncationTests.PlainText(maxOutputTokens: 50));

        using var response = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = OutputTruncationTests.AgentName, input = "hello", stream },
            TestContext.Current.CancellationToken);

        if (!stream)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
            (await TraconTestHost.ReadJsonAsync(response)).TryGetProperty("error", out _).ShouldBeTrue();
            return;
        }

        await using var body = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        var frames = await SseReader.ReadAllAsync(body);

        frames.ShouldContain(static frame => string.Equals(frame.Event, "error", StringComparison.Ordinal));
        frames.ShouldNotContain(static frame => string.Equals(frame.Event, "response.incomplete", StringComparison.Ordinal));
        frames.ShouldNotContain(static frame => string.Equals(frame.Event, "response.completed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_client_that_goes_away_mid_stream_ends_the_run_without_a_terminal_frame_or_an_error_log()
    {
        // The stalled model call is released by the disconnect alone: the request's token reaches it through
        // the rewrite layer. No terminal frame is written to a client that is gone, and nothing is logged as
        // a failure.
        var provider = new StallingModelProvider();

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(provider)
            .AddAgent(OutputTruncationTests.PlainText(maxOutputTokens: 50) with
            {
                Model = OutputTruncationTests.PlainText(maxOutputTokens: 50).Model with { Provider = StallingModelProvider.ProviderName, Model = StallingModelProvider.ModelName },
            }));

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, Responses)
        {
            Content = JsonContent.Create(new { model = OutputTruncationTests.AgentName, input = "hello", stream = true }),
        };

        using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
        await using var body = await response.Content.ReadAsStreamAsync(cancellation.Token);

        var seen = new List<string>();

        await foreach (var frame in SseReader.ReadAsync(body, cancellation.Token))
        {
            seen.Add(frame.Event ?? string.Empty);

            if (string.Equals(frame.Event, "response.output_text.delta", StringComparison.Ordinal))
            {
                break;
            }
        }

        await cancellation.CancelAsync();
        await provider.Released.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        seen.ShouldNotContain(
            static name => string.Equals(name, "response.completed", StringComparison.Ordinal)
                || string.Equals(name, "response.incomplete", StringComparison.Ordinal));

        var runs = await host.Client.GetFromJsonAsync<List<RunRecord>>(
            new Uri("/tracon/api/runs", UriKind.Relative), TestContext.Current.CancellationToken);
        var run = await host.WaitForTerminalRunAsync(runs.ShouldHaveSingleItem().Id);

        run.Status.ShouldBe(RunStatus.Canceled);
        host.Logs.Entries.ShouldNotContain(static entry =>
            entry.StartsWith("Error", StringComparison.Ordinal) || entry.StartsWith("Critical", StringComparison.Ordinal));
        host.Logs.Entries.ShouldNotContain(static entry =>
            entry.StartsWith("Warning Tracon.OpenAICompat", StringComparison.Ordinal));
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

    /// <summary>
    /// Calls <c>/v1/responses</c> for the truncation test agent and describes the answer the way an
    /// OpenAI SDK reads it: from the JSON body, or from the terminal event of the stream.
    /// </summary>
    private static async Task<ResponsesAnswer> AskAsync(TraconTestHost host, bool stream)
    {
        using var response = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = OutputTruncationTests.AgentName, input = "hello", stream },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        if (!stream)
        {
            return Describe(await TraconTestHost.ReadJsonAsync(response), frames: []);
        }

        await using var body = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        var frames = await SseReader.ReadAllAsync(body);

        frames.ShouldNotBeEmpty();

        // A stream the rewriter understood never logs its "terminal frame not found" warning.
        host.Logs.Entries.ShouldNotContain(
            static entry => entry.Contains("no response.completed frame was found", StringComparison.Ordinal));

        using var terminal = JsonDocument.Parse(frames[^1].Data);

        return Describe(terminal.RootElement.GetProperty("response"), frames);
    }

    private static ResponsesAnswer Describe(JsonElement response, List<SseFrame> frames)
    {
        var messages = response.GetProperty("output").EnumerateArray()
            .Where(static item => string.Equals(item.GetProperty("type").GetString(), "message", StringComparison.Ordinal))
            .ToList();

        var itemDone = new List<string>();
        var sequence = new List<int>();

        foreach (var frame in frames)
        {
            using var data = JsonDocument.Parse(frame.Data);

            sequence.Add(data.RootElement.GetProperty("sequence_number").GetInt32());

            if (!string.Equals(frame.Event, "response.output_item.done", StringComparison.Ordinal))
            {
                continue;
            }

            var item = data.RootElement.GetProperty("item");

            if (string.Equals(item.GetProperty("type").GetString(), "message", StringComparison.Ordinal))
            {
                itemDone.Add(item.GetProperty("status").GetString()!);
            }
        }

        return new ResponsesAnswer(
            response.GetProperty("status").GetString(),
            response.TryGetProperty("incomplete_details", out var details) && details.ValueKind == JsonValueKind.Object
                ? details.GetProperty("reason").GetString()
                : null,
            response.TryGetProperty("incomplete_details", out _),
            [.. messages.Select(static item => item.GetProperty("status").GetString()!)],
            string.Concat(messages.SelectMany(static item => item.GetProperty("content").EnumerateArray())
                .Select(static part => part.GetProperty("text").GetString())),
            [.. frames.Select(static frame => frame.Event ?? string.Empty)],
            [.. frames.Select(static frame => frame.Event ?? string.Empty)
                .Where(static name => name is "response.completed" or "response.incomplete" or "response.failed")],
            itemDone,
            sequence);
    }

    /// <summary>What a client reads from a Responses answer, whichever way it asked.</summary>
    private sealed record ResponsesAnswer(
        string? Status,
        string? IncompleteReason,
        bool HasIncompleteDetails,
        IReadOnlyList<string> MessageStatuses,
        string Text,
        IReadOnlyList<string> Events,
        IReadOnlyList<string> TerminalEvents,
        IReadOnlyList<string> MessageItemDoneStatuses,
        IReadOnlyList<int> SequenceNumbers);

    /// <summary>
    /// A provider whose stream yields a text update, then a reasoning part, then the usage with a cut-off finish
    /// reason: the order that makes MAF close the message item while the update source is still open.
    /// </summary>
    private sealed class ReasoningAfterTextModelProvider : IModelProvider
    {
        internal const string ProviderName = "reasoning-after-text";
        internal const string ModelName = "reasoning-after-text-1";

        public string Name => ProviderName;

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = ModelName }];

        public IChatClient CreateChatClient(ModelBinding binding) => new Client();

        private sealed class Client : IChatClient
        {
            public Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
                => throw new NotSupportedException("The test only streams.");

            public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                await Task.Yield();
                yield return new ChatResponseUpdate(ChatRole.Assistant, "Part of an answer");

                await Task.Yield();
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("weighing the next sentence")]);

                await Task.Yield();
                yield return new ChatResponseUpdate(
                    ChatRole.Assistant,
                    [new UsageContent(new UsageDetails { InputTokenCount = 30, OutputTokenCount = 50, TotalTokenCount = 80 })])
                {
                    FinishReason = ChatFinishReason.Length,
                };
            }

            public object? GetService(Type serviceType, object? serviceKey = null) => null;

            public void Dispose()
            {
                // Nothing to release.
            }
        }
    }

    /// <summary>
    /// A provider whose stream yields one text update and then waits for the caller to go away,
    /// so a disconnect can be made while the answer is still forming.
    /// </summary>
    private sealed class StallingModelProvider : IModelProvider
    {
        internal const string ProviderName = "stalling";
        internal const string ModelName = "stalling-1";

        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name => ProviderName;

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = ModelName }];

        /// <summary>Completes when the stalled call saw its token cancelled.</summary>
        public Task Released => _released.Task;

        public IChatClient CreateChatClient(ModelBinding binding) => new Client(this);

        private sealed class Client(StallingModelProvider owner) : IChatClient
        {
            public Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
                => throw new NotSupportedException("The test only streams.");

            public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "The first half");

                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); // delay: simulated
                }
                finally
                {
                    owner._released.TrySetResult();
                }
            }

            public object? GetService(Type serviceType, object? serviceKey = null) => null;

            public void Dispose()
            {
                // Nothing to release.
            }
        }
    }
}
