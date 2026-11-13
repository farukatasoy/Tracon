using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// A side-channel call (the compaction summarizer, an <c>aiJudge</c> judge) whose
/// binding falls back keeps that fallback to itself.
/// </summary>
/// <remarks>
/// Goes through <see cref="ModelProviderRegistry"/>, so the real
/// <c>FallbackChatClient</c> sits under the real wrapper. The end-to-end proof
/// (<c>runs.model_id</c>, the run's events and its cost) is
/// <c>HarnessLoopTests.An_aiJudge_fallback_does_not_rewrite_the_runs_model_or_its_price</c>.
/// </remarks>
public sealed class SideChannelFallbackAttributionTests
{
    private static readonly ModelBinding JudgeBinding = new()
    {
        Provider = "judge-primary",
        Model = "judge-p",
        Fallbacks = [new ModelFallback { Provider = "judge-fallback", Model = "judge-f" }],
    };

    [Fact]
    public async Task A_side_channel_fallback_leaves_the_runs_attribution_alone_and_prices_at_the_answering_model()
    {
        var answer = new ChatResponse(new ChatMessage(ChatRole.Assistant, "DONE"))
        {
            Usage = new UsageDetails { InputTokenCount = 30, OutputTokenCount = 2, TotalTokenCount = 32 },
        };
        var client = Judge(new FakeChatClient(_ => answer));
        var scope = RunScope();

        TraconRunContext.SetCurrent(scope);

        try
        {
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "judge this")], cancellationToken: Token);
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }

        AssertAttribution(scope);
    }

    [Fact]
    public async Task The_streaming_path_keeps_the_fallback_to_the_side_channel_too()
    {
        var updates = new[]
        {
            new ChatResponseUpdate(ChatRole.Assistant, "Looks "),
            new ChatResponseUpdate(ChatRole.Assistant, "finished."),
            new ChatResponseUpdate
            {
                Contents = [new UsageContent(new UsageDetails { InputTokenCount = 30, OutputTokenCount = 2, TotalTokenCount = 32 })],
            },
        };
        var client = Judge(new FakeChatClient(streamingUpdates: updates));
        var scope = RunScope();

        TraconRunContext.SetCurrent(scope);

        try
        {
            await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "judge this")], cancellationToken: Token))
            {
            }
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }

        AssertAttribution(scope);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_fallback_is_an_event_of_the_run_only_when_the_run_owns_a_model_attribution(bool ownsAttribution)
    {
        // A workflow root has an event writer and no model attribution: the model call of one of its function nodes is
        // the node's own, not the workflow's. The event on the root's stream said "the model of this run fell back",
        // which the root has none to fall back from, and nothing in the root's record changed with it.
        var store = new InMemoryRunStore();
        var writer = new RunEventWriter(store, new TraconRunRecordingOptions(), NullLogger.Instance, TraconId.NewId(), metrics: null);

        await writer.StartAsync(
            new RunStartInfo { RunId = writer.RunId, AgentName = "workflow", StartedAt = DateTimeOffset.UtcNow },
            query: "hello");

        var registry = new ModelProviderRegistry(
        [
            new FakeModelProvider(new UnreachableChatClient(), name: "judge-primary"),
            new FakeModelProvider(new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "DONE"))), name: "judge-fallback"),
        ]);

        var client = registry.CreateChatClient(JudgeBinding);

        TraconRunContext.SetCurrent(new AgentRunScope
        {
            RunId = writer.RunId,
            RootRunId = writer.RunId,
            Writer = writer,
            FallbackAttribution = ownsAttribution ? new FallbackModelAttribution() : null,
        });

        try
        {
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "go")], cancellationToken: Token);
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }

        var events = new List<RunEvent>();

        await foreach (var runEvent in store.ReadEventsAsync(writer.RunId, cancellationToken: Token))
        {
            events.Add(runEvent);
        }

        events.Count(static runEvent => runEvent.Type == RunEventType.ModelFallbackUsed).ShouldBe(
            ownsAttribution ? 1 : 0,
            "the event means that the model of THIS run fell back; a scope without a model attribution has no such model.");
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static void AssertAttribution(AgentRunScope scope)
    {
        scope.FallbackAttribution.ShouldNotBeNull().Current.ShouldBeNull(
            "the judge's fallback is not the run's: runs.model_id must keep the agent's model.");

        var share = scope.ExtraUsage.ShouldNotBeNull().ToOtherModelUsages().ShouldHaveSingleItem();
        share.Provider.ShouldBe("judge-fallback");
        share.Model.ShouldBe("judge-f");
        share.Usage.InputTokens.ShouldBe(30);
        share.Usage.OutputTokens.ShouldBe(2);

        scope.ExtraUsage.ToAgentModelUsage().ShouldBeNull();
    }

    private static AgentRunScope RunScope()
    {
        var runId = Guid.NewGuid();

        return new AgentRunScope
        {
            RunId = runId,
            RootRunId = runId,
            ExtraUsage = new SideChannelUsageAccumulator(),
            FallbackAttribution = new FallbackModelAttribution(),
        };
    }

    private static SideChannelUsageTrackingChatClient Judge(IChatClient fallback)
    {
        var registry = new ModelProviderRegistry(
        [
            new FakeModelProvider(new UnreachableChatClient(), name: "judge-primary"),
            new FakeModelProvider(fallback, name: "judge-fallback"),
        ]);

        return SideChannelUsageTrackingChatClient.ForLoopJudge(registry.CreateChatClient(JudgeBinding), JudgeBinding);
    }

    /// <summary>Fails every call with a connection error, which the fallback chain moves past.</summary>
    private sealed class UnreachableChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw Refused();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            throw Refused();
#pragma warning disable CS0162 // The iterator needs a yield statement to compile as one.
            yield break;
#pragma warning restore CS0162
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // The fake client has no resource to release.
        }

        private static HttpRequestException Refused()
            => new(HttpRequestError.ConnectionError, "Connection refused.");
    }
}
