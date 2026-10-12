using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// A side-channel call (the compaction summarizer, an <c>aiJudge</c> judge) whose
/// binding falls back keeps that fallback to itself (F-360).
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
