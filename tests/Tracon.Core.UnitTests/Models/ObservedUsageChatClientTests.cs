using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// <see cref="ObservedUsageChatClient"/> adds every response the provider billed to the running run's
/// accumulator, once, and leaves anything that is not the agent's own call alone.
/// </summary>
public sealed class ObservedUsageChatClientTests
{
    [Fact]
    public async Task A_buffered_response_adds_its_usage_to_the_running_runs_accumulator()
    {
        var observed = new ObservedUsageAccumulator();
        var client = ClientOver(new FakeChatClient(_ => Reply(Usage(input: 120, output: 30))));

        await CallAsync(client, new AgentRunScope { RunId = TraconId.NewId(), RootRunId = TraconId.NewId(), ObservedUsage = observed });

        var usage = observed.ToRunUsage().ShouldNotBeNull();
        usage.InputTokens.ShouldBe(120);
        usage.OutputTokens.ShouldBe(30);
        usage.TotalTokens.ShouldBe(150);
    }

    [Fact]
    public async Task Every_call_of_a_tool_loop_is_added_once()
    {
        var observed = new ObservedUsageAccumulator();
        var client = ClientOver(new FakeChatClient(_ => Reply(Usage(input: 100, output: 10))));
        var scope = new AgentRunScope { RunId = TraconId.NewId(), RootRunId = TraconId.NewId(), ObservedUsage = observed };

        await CallAsync(client, scope);
        await CallAsync(client, scope);
        await CallAsync(client, scope);

        var usage = observed.ToRunUsage().ShouldNotBeNull();
        usage.InputTokens.ShouldBe(300);
        usage.OutputTokens.ShouldBe(30);
    }

    [Fact]
    public async Task A_stream_that_reports_usage_in_two_parts_adds_their_sum()
    {
        var observed = new ObservedUsageAccumulator();
        var client = ClientOver(new FakeChatClient(streamingUpdates:
        [
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("first"), new UsageContent(Usage(input: 70, output: 0))]),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("second"), new UsageContent(Usage(input: 0, output: 25))]),
        ]));

        await DrainAsync(client, new AgentRunScope { RunId = TraconId.NewId(), RootRunId = TraconId.NewId(), ObservedUsage = observed });

        var usage = observed.ToRunUsage().ShouldNotBeNull();
        usage.InputTokens.ShouldBe(70);
        usage.OutputTokens.ShouldBe(25);
    }

    [Fact]
    public async Task A_stream_that_fails_after_its_usage_part_keeps_the_usage_it_reported()
    {
        var observed = new ObservedUsageAccumulator();
        var client = ClientOver(new FailingStreamClient(Usage(input: 80, output: 8)));

        await Should.ThrowAsync<InvalidOperationException>(
            () => DrainAsync(client, new AgentRunScope { RunId = TraconId.NewId(), RootRunId = TraconId.NewId(), ObservedUsage = observed }));

        observed.ToRunUsage().ShouldNotBeNull().InputTokens.ShouldBe(80);
    }

    [Fact]
    public async Task A_stream_the_consumer_abandons_keeps_the_usage_that_already_passed()
    {
        var observed = new ObservedUsageAccumulator();
        var client = ClientOver(new FakeChatClient(streamingUpdates:
        [
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("first"), new UsageContent(Usage(input: 40, output: 4))]),
            new ChatResponseUpdate(ChatRole.Assistant, "never read"),
        ]));

        var scope = new AgentRunScope { RunId = TraconId.NewId(), RootRunId = TraconId.NewId(), ObservedUsage = observed };

        TraconRunContext.SetCurrent(scope);

        try
        {
            await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "go")], cancellationToken: TestContext.Current.CancellationToken))
            {
                break;
            }
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }

        observed.ToRunUsage().ShouldNotBeNull().InputTokens.ShouldBe(40);
    }

    [Fact]
    public async Task A_response_that_reports_no_usage_leaves_the_accumulator_empty_not_zero()
    {
        var observed = new ObservedUsageAccumulator();
        var client = ClientOver(new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")) { Usage = null }));

        await CallAsync(client, new AgentRunScope { RunId = TraconId.NewId(), RootRunId = TraconId.NewId(), ObservedUsage = observed });

        observed.ToRunUsage().ShouldBeNull();
    }

    [Fact]
    public async Task A_side_channel_call_is_left_to_the_side_channel_accumulator()
    {
        // The compaction summarizer and the loop judge run through this pipeline too. Their calls carry the
        // side-channel marker in the scope, and their tokens are counted under the model that answered them.
        var observed = new ObservedUsageAccumulator();
        var client = ClientOver(new FakeChatClient(_ => Reply(Usage(input: 500, output: 50))));

        await CallAsync(client, new AgentRunScope
        {
            RunId = TraconId.NewId(),
            RootRunId = TraconId.NewId(),
            ObservedUsage = observed,
            SideChannelFallback = new FallbackModelAttribution(),
        });

        observed.ToRunUsage().ShouldBeNull();
    }

    [Fact]
    public async Task A_call_outside_a_run_passes_through()
    {
        var client = ClientOver(new FakeChatClient(_ => Reply(Usage(input: 10, output: 1))));

        TraconRunContext.SetCurrent(null);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "go")], cancellationToken: TestContext.Current.CancellationToken);

        response.Text.ShouldBe("ok");
    }

    [Fact]
    public async Task A_scope_without_an_accumulator_passes_through()
    {
        var client = ClientOver(new FakeChatClient(_ => Reply(Usage(input: 10, output: 1))));

        await CallAsync(client, new AgentRunScope { RunId = TraconId.NewId(), RootRunId = TraconId.NewId() });
    }

    [Fact]
    public async Task Parallel_runs_add_to_their_own_accumulator()
    {
        var firstObserved = new ObservedUsageAccumulator();
        var secondObserved = new ObservedUsageAccumulator();

        // Both calls are in flight at the same time: neither answers before the other has been asked.
        var gate = new ArrivalGate(2);

        var first = Task.Run(() => RunAsync(
            ClientOver(new GatedChatClient(gate, Usage(input: 1_000, output: 100))),
            new AgentRunScope { RunId = TraconId.NewId(), RootRunId = TraconId.NewId(), ObservedUsage = firstObserved }));

        var second = Task.Run(() => RunAsync(
            ClientOver(new GatedChatClient(gate, Usage(input: 7, output: 3))),
            new AgentRunScope { RunId = TraconId.NewId(), RootRunId = TraconId.NewId(), ObservedUsage = secondObserved }));

        await Task.WhenAll(first, second).WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

        firstObserved.ToRunUsage().ShouldNotBeNull().InputTokens.ShouldBe(1_000);
        secondObserved.ToRunUsage().ShouldNotBeNull().InputTokens.ShouldBe(7);

        // The scope is set in the body of the method that starts the run, as the run recorder does.
        static async Task RunAsync(ObservedUsageChatClient client, AgentRunScope scope)
        {
            TraconRunContext.SetCurrent(scope);

            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "go")], cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    // ---- fixtures ----

    private static ObservedUsageChatClient ClientOver(IChatClient inner) => new(inner);

    private static ChatResponse Reply(UsageDetails usage) => new(new ChatMessage(ChatRole.Assistant, "ok")) { Usage = usage };

    private static UsageDetails Usage(long input, long output)
        => new() { InputTokenCount = input, OutputTokenCount = output, TotalTokenCount = input + output };

    private static async Task CallAsync(ObservedUsageChatClient client, AgentRunScope scope)
    {
        TraconRunContext.SetCurrent(scope);

        try
        {
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "go")], cancellationToken: TestContext.Current.CancellationToken);
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }
    }

    private static async Task DrainAsync(ObservedUsageChatClient client, AgentRunScope scope)
    {
        TraconRunContext.SetCurrent(scope);

        try
        {
            await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "go")], cancellationToken: TestContext.Current.CancellationToken))
            {
                // Draining is enough to drive the stream.
            }
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
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

    private sealed class GatedChatClient(ArrivalGate gate, UsageDetails usage) : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            await gate.ArriveAsync().ConfigureAwait(false);

            return Reply(usage);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("this fixture is buffered-only");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>A stream that reports its usage in its first part and then fails.</summary>
    private sealed class FailingStreamClient(UsageDetails usage) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("this fixture is streaming-only");

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("partial"), new UsageContent(usage)]);

            await Task.Yield();

            throw new InvalidOperationException("the provider dropped the stream");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
