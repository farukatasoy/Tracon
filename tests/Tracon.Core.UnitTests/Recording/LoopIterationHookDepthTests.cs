using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// The hook that streams a loop's iteration frames belongs to the run the caller opened: the scope of a run below it does
/// not carry it, even when its options were copied from the caller's.
/// </summary>
/// <remarks>
/// A sub-agent's loop writes to its own run (<c>LoopIterationFrameTests</c> proves it through the whole host). That test
/// stays green when the guard in <c>RunRecordingAgent.PrepareRun</c> is removed, because the options of a child are built
/// without the hook today; <see cref="TraconRunOptions"/> copies it, so a child path that is added with a copy is what the
/// guard is for. This test hands the hook to a run at depth one the way a copy would.
/// </remarks>
public sealed class LoopIterationHookDepthTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(3, false)]
    public async Task The_scope_of_a_run_carries_the_loop_hook_only_at_the_root(int depth, bool carried)
    {
        var inner = new ScopeCapturingAgent();
        var tenant = new FixedTenantContext();
        var agent = TestRunRecording.Agent(
            inner,
            new InMemoryRunStore(tenantContext: tenant),
            tenant,
            new TraconRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance);

        Func<RunEvent, CancellationToken, ValueTask> hook = static (_, _) => default;

        await agent.RunAsync(
            "hello",
            options: new TraconRunOptions { RunId = Guid.NewGuid(), Depth = depth, AfterLoopIterationRecorded = hook });

        var seen = inner.Seen.ShouldNotBeNull();

        (seen.AfterLoopIterationRecorded is not null).ShouldBe(
            carried,
            carried
                ? "the root run streams its loop's frames through the hook"
                : "a run below the root writes its loop to its own record and streams nothing to the stream above it");
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public string TenantId => "default";
    }

    private sealed class ScopeCapturingAgent : AIAgent
    {
        public AgentRunScope? Seen { get; private set; }

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            System.Text.Json.JsonElement serializedState,
            System.Text.Json.JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        protected override ValueTask<System.Text.Json.JsonElement> SerializeSessionCoreAsync(
            AgentSession session,
            System.Text.Json.JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Seen = TraconRunContext.Current;

            return Task.FromResult(new AgentResponse(new ChatMessage(ChatRole.Assistant, "done")));
        }

        protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Seen = TraconRunContext.Current;
            await Task.CompletedTask;
            yield return new AgentResponseUpdate(ChatRole.Assistant, "done");
        }
    }
}
