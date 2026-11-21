using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// A run that ends in an exception or a cancellation keeps the tokens the provider billed before it ended: on its row, in
/// its cost, and in the number the quota counts.
/// </summary>
/// <remarks>
/// <para>
/// The run recorder reads a run's usage from its answer, and such a run has none, so its row said it spent nothing
/// although the provider had billed every call that came before the failure. The model pipeline now counts each response as
/// it arrives (<see cref="ObservedUsageChatClient"/>) and the recorder reads that count for a run that ends without an
/// answer.
/// </para>
/// <para>
/// Every scenario runs the real pipeline: <see cref="AgentDefinitionCompiler"/>, <see cref="ModelProviderRegistry"/>, the
/// real tool-call loop and the real recorder. The provider is scripted, so a call fails on the turn the test names.
/// </para>
/// </remarks>
public sealed class FailedRunUsageTests
{
    private const string ToolName = "lookup";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_run_that_fails_on_its_third_model_call_keeps_the_tokens_of_the_two_calls_before(bool streaming)
    {
        var client = new ScriptedChatClient(Turn.Tool(100, 10), Turn.Tool(200, 20), Turn.Fails(new InvalidOperationException("the provider is down")));
        var resolver = new FakeRunPricingResolver((_, _, usage) => new RunCost { Source = PricingSource.Catalog, InputCost = (usage?.InputTokens ?? 0) / 1_000m });

        var harness = new Harness(client, resolver);

        (await harness.RunToTheEndAsync(streaming)).ShouldNotBeNull();

        client.CallCount.ShouldBe(3);

        var run = await harness.SingleRunAsync();

        run.Status.ShouldBe(RunStatus.Failed);

        var usage = run.Usage.ShouldNotBeNull();
        usage.InputTokens.ShouldBe(300);
        usage.OutputTokens.ShouldBe(30);
        usage.TotalTokens.ShouldBe(330);

        // The cost is calculated from the same usage: 300 input tokens at 1 per thousand.
        run.Cost.ShouldNotBeNull().InputCost.ShouldBe(0.300m);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_run_that_fails_on_its_first_call_records_no_usage_not_zero(bool streaming)
    {
        var client = new ScriptedChatClient(Turn.Fails(new InvalidOperationException("the provider is down")));
        var harness = new Harness(client);

        (await harness.RunToTheEndAsync(streaming)).ShouldNotBeNull();

        var run = await harness.SingleRunAsync();

        run.Status.ShouldBe(RunStatus.Failed);
        run.Usage.ShouldBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_provider_that_reports_no_usage_before_the_failure_leaves_the_usage_null(bool streaming)
    {
        var client = new ScriptedChatClient(Turn.ToolWithoutUsage(), Turn.ToolWithoutUsage(), Turn.Fails(new InvalidOperationException("the provider is down")));
        var harness = new Harness(client);

        (await harness.RunToTheEndAsync(streaming)).ShouldNotBeNull();

        var run = await harness.SingleRunAsync();

        run.Status.ShouldBe(RunStatus.Failed);
        run.Usage.ShouldBeNull();
    }

    [Fact]
    public async Task A_stream_that_fails_after_its_usage_part_keeps_that_part()
    {
        var client = new ScriptedChatClient(Turn.Tool(100, 10), Turn.ReplyThenFails(50, 5, new InvalidOperationException("the provider dropped the stream")));
        var harness = new Harness(client);

        (await harness.RunToTheEndAsync(streaming: true)).ShouldNotBeNull();

        var run = await harness.SingleRunAsync();

        run.Status.ShouldBe(RunStatus.Failed);
        run.Usage.ShouldNotBeNull().InputTokens.ShouldBe(150);
    }

    [Fact]
    public async Task A_streamed_run_canceled_after_a_usage_part_keeps_the_usage()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new ScriptedChatClient(Turn.Tool(100, 10), Turn.Tool(200, 20), Turn.Answer(50, 5));
        var harness = new Harness(client);

        var updates = 0;

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var update in harness.Agent.RunStreamingAsync("go", cancellationToken: cancellation.Token))
            {
                // The first call has reported its usage when its update reaches the consumer.
                if (++updates == 1)
                {
                    await cancellation.CancelAsync();
                }
            }
        });

        var run = await harness.SingleRunAsync();

        run.Status.ShouldBe(RunStatus.Canceled);
        run.Usage.ShouldNotBeNull().InputTokens.ShouldBe(100);
    }

    [Fact]
    public async Task A_streamed_run_canceled_before_any_usage_arrived_records_no_usage_not_zero()
    {
        using var cancellation = new CancellationTokenSource();

        // The first part is text only; the usage comes with the part that is never read.
        var client = new ScriptedChatClient(Turn.TextThenUsage(100, 10));
        var harness = new Harness(client);

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var update in harness.Agent.RunStreamingAsync("go", cancellationToken: cancellation.Token))
            {
                await cancellation.CancelAsync();
            }
        });

        var run = await harness.SingleRunAsync();

        run.Status.ShouldBe(RunStatus.Canceled);
        run.Usage.ShouldBeNull();
    }

    [Fact]
    public async Task A_buffered_run_canceled_between_two_calls_keeps_the_first_calls_usage()
    {
        using var cancellation = new CancellationTokenSource();

        // The second call finds the token canceled by the tool the first call asked for.
        var client = new ScriptedChatClient(Turn.Tool(100, 10), Turn.Answer(200, 20));
        var harness = new Harness(client, onTool: () => cancellation.Cancel());

        await Should.ThrowAsync<OperationCanceledException>(() => harness.Agent.RunAsync("go", cancellationToken: cancellation.Token));

        var run = await harness.SingleRunAsync();

        run.Status.ShouldBe(RunStatus.Canceled);
        run.Usage.ShouldNotBeNull().InputTokens.ShouldBe(100);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_run_that_completes_keeps_the_usage_of_its_answer_and_it_equals_what_the_pipeline_observed(bool streaming)
    {
        var client = new ScriptedChatClient(Turn.Tool(100, 10), Turn.Tool(200, 20), Turn.Answer(50, 5));
        var harness = new Harness(client);

        AgentResponse? response = null;

        if (streaming)
        {
            await foreach (var _ in harness.Agent.RunStreamingAsync("go", cancellationToken: TestContext.Current.CancellationToken))
            {
                // Draining is enough to drive the stream.
            }
        }
        else
        {
            response = await harness.Agent.RunAsync("go", cancellationToken: TestContext.Current.CancellationToken);
        }

        var run = await harness.SingleRunAsync();

        run.Status.ShouldBe(RunStatus.Completed);

        var usage = run.Usage.ShouldNotBeNull();
        usage.InputTokens.ShouldBe(350);
        usage.OutputTokens.ShouldBe(35);

        // The usage of the answer is the total of the calls the pipeline observed, so a run that completes is not
        // changed by the new count: the two are equal, and the run keeps the answer's.
        var observed = harness.ObservedByTheLastRun.ShouldNotBeNull().ToRunUsage().ShouldNotBeNull();
        observed.InputTokens.ShouldBe(usage.InputTokens);
        observed.OutputTokens.ShouldBe(usage.OutputTokens);
        observed.TotalTokens.ShouldBe(usage.TotalTokens);

        if (!streaming)
        {
            response.ShouldNotBeNull().Usage.ShouldNotBeNull().InputTokenCount.ShouldBe(usage.InputTokens);
        }
    }

    [Fact]
    public async Task A_fallback_links_answer_is_counted_once_and_the_failed_attempt_counts_nothing()
    {
        var primary = new ScriptedChatClient(Turn.Fails(new TraconProviderUnavailableException("circuit open")));
        var link = new ScriptedChatClient(Turn.Tool(100, 10), Turn.Fails(new InvalidOperationException("the link is down too")));
        var harness = new Harness(primary, link: link);

        (await harness.RunToTheEndAsync(streaming: false)).ShouldNotBeNull();

        var run = await harness.SingleRunAsync();

        run.Status.ShouldBe(RunStatus.Failed);

        // The first call failed on the primary and was answered by the link; the next call failed on the link.
        run.Usage.ShouldNotBeNull().InputTokens.ShouldBe(100);
    }

    [Fact]
    public async Task Parallel_failed_runs_keep_the_usage_of_their_own_calls()
    {
        // Both runs have made their first call when either goes on: neither can finish before the other has started.
        var gate = new ArrivalGate(2);

        var first = new Harness(
            new ScriptedChatClient(Turn.Tool(1_000, 100), Turn.Fails(new InvalidOperationException("down"))) { BeforeCall = call => call == 2 ? gate.ArriveAsync() : Task.CompletedTask },
            agentName: "first-agent");

        var second = new Harness(
            new ScriptedChatClient(Turn.Tool(7, 3), Turn.Fails(new InvalidOperationException("down"))) { BeforeCall = call => call == 2 ? gate.ArriveAsync() : Task.CompletedTask },
            agentName: "second-agent");

        var runs = await Task.WhenAll(
                Task.Run(() => first.RunToTheEndAsync(streaming: false)),
                Task.Run(() => second.RunToTheEndAsync(streaming: false)))
            .WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

        runs.ShouldAllBe(static exception => exception != null);

        (await first.SingleRunAsync()).Usage.ShouldNotBeNull().InputTokens.ShouldBe(1_000);
        (await second.SingleRunAsync()).Usage.ShouldNotBeNull().InputTokens.ShouldBe(7);
    }

    [Fact]
    public async Task The_side_channel_share_of_a_failed_run_is_still_added_to_what_the_agents_own_model_spent()
    {
        // The accumulator of the side channel (a compaction summarizer, a loop judge) holds the share of ANOTHER
        // model; the run keeps it beside the agent's own count.
        var client = new ScriptedChatClient(Turn.Tool(100, 10), Turn.Fails(new InvalidOperationException("the provider is down")));
        var harness = new Harness(client, onTool: () =>
        {
            TraconRunContext.Current.ShouldNotBeNull().ExtraUsage.ShouldNotBeNull()
                .Add(new UsageDetails { InputTokenCount = 40, OutputTokenCount = 4, TotalTokenCount = 44 }, "judge", "judge-model");
        });

        (await harness.RunToTheEndAsync(streaming: false)).ShouldNotBeNull();

        var run = await harness.SingleRunAsync();

        run.Status.ShouldBe(RunStatus.Failed);
        run.Usage.ShouldNotBeNull().InputTokens.ShouldBe(140);
    }

    // ---- fixtures ----

    private sealed class Harness
    {
        private readonly InMemoryRunStore _store = new(tenantContext: FixedTenantContext.Default);
        private readonly Action? _onTool;

        public Harness(
            ScriptedChatClient client,
            IRunPricingResolver? resolver = null,
            Action? onTool = null,
            ScriptedChatClient? link = null,
            string agentName = "test-agent")
        {
            _onTool = onTool;

            // The tool captures the accumulator of the run it runs in: it is called by the tool-call loop, inside the run.
            var tool = AIFunctionFactory.Create(
                () =>
                {
                    ObservedByTheLastRun = TraconRunContext.Current?.ObservedUsage;
                    _onTool?.Invoke();

                    return "result";
                },
                ToolName,
                "test tool");

            var providers = link is null
                ? new[] { new FakeModelProvider(client) }
                : [new FakeModelProvider(client, name: "primary"), new FakeModelProvider(link, name: "link")];

            var compiler = new AgentDefinitionCompiler(
                TestData.Providers(providers),
                TestData.Registry(tool));

            var definition = TestData.Definition(name: agentName, toolNames: [ToolName]);

            if (link is not null)
            {
                definition = definition with
                {
                    Model = new ModelBinding
                    {
                        Provider = "primary",
                        Model = "fake-model",
                        Fallbacks = [new ModelFallback { Provider = "link", Model = "fake-model" }],
                    },
                };
            }

            Agent = TestRunRecording.Agent(
                compiler.Compile(definition),
                _store,
                FixedTenantContext.Default,
                new TraconRunRecordingOptions(),
                NullLogger<RunRecordingAgent>.Instance,
                pricingResolver: resolver);
        }

        public RunRecordingAgent Agent { get; }

        /// <summary>The accumulator the last run's tool saw in its own scope.</summary>
        public ObservedUsageAccumulator? ObservedByTheLastRun { get; private set; }

        public async Task<RunRecord> SingleRunAsync()
            => (await _store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        /// <summary>Runs the agent and returns the exception the run ended with.</summary>
        public async Task<Exception?> RunToTheEndAsync(bool streaming)
        {
            try
            {
                if (streaming)
                {
                    await foreach (var _ in Agent.RunStreamingAsync("go", cancellationToken: TestContext.Current.CancellationToken))
                    {
                        // Draining is enough to drive the stream.
                    }
                }
                else
                {
                    await Agent.RunAsync("go", cancellationToken: TestContext.Current.CancellationToken);
                }
            }
            catch (Exception exception)
            {
                return exception;
            }

            return null;
        }
    }

    /// <summary>Releases every waiting call once the expected number of calls has arrived.</summary>
    private sealed class ArrivalGate(int expected)
    {
        private readonly TaskCompletionSource _all = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public Task ArriveAsync()
        {
            if (Interlocked.Increment(ref _arrived) == expected)
            {
                _all.TrySetResult();
            }

            return _all.Task;
        }
    }

    /// <summary>One model call of a script: what the provider answers, or how it fails.</summary>
    private sealed record Turn(
        long? InputTokens,
        long? OutputTokens,
        bool CallsTool,
        Exception? Failure = null,
        bool FailsAfterReply = false,
        bool UsageInSecondPart = false)
    {
        /// <summary>A turn that asks for the tool and reports its usage.</summary>
        public static Turn Tool(long input, long output) => new(input, output, CallsTool: true);

        /// <summary>A turn that asks for the tool and reports no usage.</summary>
        public static Turn ToolWithoutUsage() => new(null, null, CallsTool: true);

        /// <summary>A turn that answers and reports its usage.</summary>
        public static Turn Answer(long input, long output) => new(input, output, CallsTool: false);

        /// <summary>A call that fails and reports nothing.</summary>
        public static Turn Fails(Exception failure) => new(null, null, CallsTool: false, failure);

        /// <summary>A streamed call that reports its usage with its first part, then fails.</summary>
        public static Turn ReplyThenFails(long input, long output, Exception failure) => new(input, output, CallsTool: true, failure, FailsAfterReply: true);

        /// <summary>A streamed answer whose first part is text and whose second part reports the usage.</summary>
        public static Turn TextThenUsage(long input, long output) => new(input, output, CallsTool: false, UsageInSecondPart: true);

        public UsageDetails? Usage()
            => InputTokens is null
                ? null
                : new UsageDetails { InputTokenCount = InputTokens, OutputTokenCount = OutputTokens, TotalTokenCount = InputTokens + OutputTokens };
    }

    /// <summary>
    /// A model provider's client that answers a script, one turn per call, and repeats the last turn when the script runs out.
    /// </summary>
    private sealed class ScriptedChatClient(params Turn[] turns) : IChatClient
    {
        private int _calls;

        public int CallCount => Volatile.Read(ref _calls);

        /// <summary>Runs before a call is answered, with its 1-based number; the gate of a parallel scenario.</summary>
        public Func<int, Task>? BeforeCall { get; init; }

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _calls);

            if (BeforeCall is not null)
            {
                await BeforeCall(call).ConfigureAwait(false);
            }

            // A provider SDK does not send a request whose token is already canceled.
            cancellationToken.ThrowIfCancellationRequested();

            var turn = TurnOf(call);

            if (turn.Failure is not null)
            {
                throw turn.Failure;
            }

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, Contents(turn, call))) { Usage = turn.Usage() };
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _calls);

            if (BeforeCall is not null)
            {
                await BeforeCall(call).ConfigureAwait(false);
            }

            var turn = TurnOf(call);

            if (turn.Failure is not null && !turn.FailsAfterReply)
            {
                throw turn.Failure;
            }

            if (turn.UsageInSecondPart)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "partial text");

                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();

                yield return new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(turn.Usage()!)]);

                yield break;
            }

            var contents = Contents(turn, call);

            if (turn.Usage() is { } usage)
            {
                contents.Add(new UsageContent(usage));
            }

            yield return new ChatResponseUpdate(ChatRole.Assistant, contents);

            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();

            if (turn.Failure is not null)
            {
                throw turn.Failure;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }

        private Turn TurnOf(int call) => turns[Math.Min(call, turns.Length) - 1];

        private static List<AIContent> Contents(Turn turn, int call)
            => turn.CallsTool
                ? [new FunctionCallContent($"call-{call}", ToolName, new Dictionary<string, object?>(StringComparer.Ordinal))]
                : [new TextContent("done")];
    }
}
