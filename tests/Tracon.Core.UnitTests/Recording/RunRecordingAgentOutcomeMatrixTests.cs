using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// Phase 107 outcome matrix: proves the streaming and the non-streaming entry
/// point reach the SAME status, terminal event, cancellation cleanup, and
/// span closure for every outcome a root run can end in.
/// </summary>
/// <remarks>
/// The phase 107 split moved every private helper of <see cref="RunRecordingAgent"/>
/// (run preparation, event writing, completion, notifications) into separate
/// partial files, but left <c>RunCoreAsync</c>/<c>RunCoreStreamingAsync</c>
/// untouched. A helper move that quietly changed the behavior of only ONE of
/// the two entry points - for example a completion detail that stayed correct
/// for the buffered path but drifted for the streaming one - would compile
/// and pass every single-path test. This matrix runs the same four outcomes
/// through both paths and asserts on the same observable facts, so such a
/// drift fails here instead of in production.
/// </remarks>
public sealed class RunRecordingAgentOutcomeMatrixTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Completed_run_reaches_the_same_outcome_on_both_paths(bool streaming)
    {
        using var harness = new Harness();
        var agent = harness.CreateAgent(new FakeChatClient());

        await Harness.RunAsync(agent, streaming);

        var run = await harness.SingleRunAsync();
        run.Status.ShouldBe(RunStatus.Completed);

        (await harness.LastEventTypeAsync(run.Id)).ShouldBe(RunEventType.RunCompleted);
        harness.Registry.ActiveCount.ShouldBe(0);
        (await harness.RunSpanStatusTagAsync(run.Id)).ShouldBe(nameof(RunStatus.Completed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_run_reaches_the_same_outcome_on_both_paths(bool streaming)
    {
        // A content-filtered empty response is the smallest deterministic
        // trigger available on BOTH paths without a raw exception - the
        // streaming client has no hook to throw mid-stream (RunRecordingAgentTests
        // exercises this same trigger on each path separately).
        var client = ContentFilteredClient(streaming);

        using var harness = new Harness();
        var agent = harness.CreateAgent(client);

        await Should.ThrowAsync<TraconContentFilteredException>(() => Harness.RunAsync(agent, streaming));

        var run = await harness.SingleRunAsync();
        run.Status.ShouldBe(RunStatus.Failed);

        (await harness.LastEventTypeAsync(run.Id)).ShouldBe(RunEventType.RunFailed);
        harness.Registry.ActiveCount.ShouldBe(0);
        (await harness.RunSpanStatusTagAsync(run.Id)).ShouldBe(nameof(RunStatus.Failed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Canceled_run_reaches_the_same_outcome_on_both_paths(bool streaming)
    {
        using var harness = new Harness();
        var agent = harness.CreateAgent(new BlockingChatClient());

        var runTask = streaming
            ? Task.Run(async () =>
              {
                  await foreach (var _ in agent.RunStreamingAsync("write a long piece of text"))
                  {
                      // BlockingChatClient never produces a second frame; the loop
                      // just keeps the enumerator alive like a real client would.
                  }
              })
            : agent.RunAsync("write a long piece of text");

        // The registry entry is written BEFORE the run row - RunRecordingAgent
        // registers first so a cancel can land during the start write. Waiting
        // for the entry alone raced the read below (Phase 184).
        await WaitUntil.TrueAsync(async () =>
            harness.Registry.ActiveCount == 1 && (await harness.Store.QueryRunsAsync(new RunQuery())).Count == 1);

        var started = await harness.SingleRunAsync();
        harness.Registry.TryCancel(started.Id, "test").ShouldBeTrue();

        await Should.ThrowAsync<OperationCanceledException>(() => runTask);

        var run = await harness.Store.GetRunAsync(started.Id);
        run!.Status.ShouldBe(RunStatus.Canceled);

        (await harness.LastEventTypeAsync(run.Id)).ShouldBe(RunEventType.RunFailed);
        harness.Registry.ActiveCount.ShouldBe(0);
        (await harness.RunSpanStatusTagAsync(run.Id)).ShouldBe(nameof(RunStatus.Canceled));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AwaitingApproval_run_reaches_the_same_outcome_on_both_paths(bool streaming)
    {
        var approval = new ToolApprovalRequestContent(
            "req-1",
            new FunctionCallContent("call-1", "delete_order", new Dictionary<string, object?>(StringComparer.Ordinal)));

        var client = streaming
            ? new FakeChatClient(streamingUpdates: [new ChatResponseUpdate(ChatRole.Assistant, [approval])])
            : new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, [approval])));

        using var harness = new Harness();
        var agent = harness.CreateAgent(client);

        await Harness.RunAsync(agent, streaming);

        var run = await harness.SingleRunAsync();
        run.Status.ShouldBe(RunStatus.AwaitingApproval);

        var closingEvent = await harness.LastEventAsync(run.Id);

        closingEvent.Type.ShouldBe(RunEventType.RunAwaitingInput);
        harness.Registry.ActiveCount.ShouldBe(0);
        (await harness.RunSpanStatusTagAsync(run.Id)).ShouldBe(nameof(RunStatus.AwaitingApproval));

        // Phase 142: Payload carries one entry per pending request — no
        // IToolApprovalPresenter is registered in this harness, so the
        // presentation fields stay absent and only the raw request survives.
        closingEvent.Payload.ShouldNotBeNull();
        closingEvent.Payload.ShouldContain("\"requestId\":\"req-1\"");
        closingEvent.Payload.ShouldContain("\"toolName\":\"delete_order\"");
        closingEvent.Payload.ShouldNotContain("entityName");
    }

    private static FakeChatClient ContentFilteredClient(bool streaming)
    {
        if (streaming)
        {
            return new FakeChatClient(streamingUpdates:
            [
                new ChatResponseUpdate(ChatRole.Assistant, string.Empty) { FinishReason = ChatFinishReason.ContentFilter },
            ]);
        }

        return new FakeChatClient(_ => new ChatResponse
        {
            Messages = [new ChatMessage(ChatRole.Assistant, string.Empty)],
            FinishReason = ChatFinishReason.ContentFilter,
        });
    }

    private sealed class Harness : IDisposable
    {
        private readonly InMemoryTraceStore _traceStore;
        private readonly RunTraceCollector _traceCollector;

        public Harness()
        {
            Store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
            Registry = new RunCancellationRegistry();
            _traceStore = new InMemoryTraceStore(new FixedTenantContext());

            var options = new TraconOptions();

            // Every outcome must persist its span, not just the failed ones -
            // the default 10% success sample would make this matrix flaky.
            options.Observability.SuccessSampleRatio = 1;

            _traceCollector = new RunTraceCollector(
                _traceStore,
                Options.Create(options),
                NullLogger<RunTraceCollector>.Instance);
        }

        public InMemoryRunStore Store { get; }

        public RunCancellationRegistry Registry { get; }

        public RunRecordingAgent CreateAgent(IChatClient client)
        {
            var compiler = new AgentDefinitionCompiler(
                TestData.Providers(new FakeModelProvider(client)),
                TestData.Registry());

            return new RunRecordingAgent(
                compiler.Compile(TestData.Definition()),
                Store,
                new FixedTenantContext(),
                new TraconRunRecordingOptions(),
                NullLogger<RunRecordingAgent>.Instance,
                traceCollector: _traceCollector,
                cancellationRegistry: Registry);
        }

        public static async Task RunAsync(RunRecordingAgent agent, bool streaming)
        {
            if (streaming)
            {
                await foreach (var _ in agent.RunStreamingAsync("hello"))
                {
                    // Frames are drained; only the terminal record matters here.
                }

                return;
            }

            await agent.RunAsync("hello");
        }

        public async Task<RunRecord> SingleRunAsync()
            => (await Store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        public async Task<RunEventType> LastEventTypeAsync(Guid runId) => (await LastEventAsync(runId)).Type;

        public async Task<RunEvent> LastEventAsync(Guid runId)
        {
            RunEvent? last = null;

            await foreach (var runEvent in Store.ReadEventsAsync(runId))
            {
                last = runEvent;
            }

            return last ?? throw new InvalidOperationException($"Run {runId} has no events.");
        }

        public async Task<string?> RunSpanStatusTagAsync(Guid runId)
        {
            var trace = await _traceStore.GetTraceByRunAsync(runId);

            trace.ShouldNotBeNull($"run {runId} must own and persist its trace (root run, SuccessSampleRatio=1).");

            var root = trace.Spans.Single(
                span => string.Equals(span.Name, TraconDiagnostics.RunActivityName, StringComparison.Ordinal));

            return root.Attributes.GetValueOrDefault(TraconDiagnostics.Tags.Status);
        }

        public void Dispose() => _traceCollector.Dispose();
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public string TenantId => "test";
    }
}
