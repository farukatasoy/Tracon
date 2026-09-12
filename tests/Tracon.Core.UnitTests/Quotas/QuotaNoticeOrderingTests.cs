using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Quotas;

/// <summary>
/// The sequence-ordering guarantee phase 146 relies on:
/// <see cref="RunEventWriter.AppendReservedAsync"/> called BEFORE
/// <see cref="RunEventWriter.CompleteAsync"/> gets a lower sequence number
/// than the terminal event, and a reader that starts from the beginning of
/// the stream sees it in that same order. <c>RunRecordingAgent.CompleteAsync</c>
/// writes the quota threshold notice this way (146.1) precisely so the notice
/// lands inside the run's own append-only stream instead of after it closes.
/// </summary>
public sealed class QuotaNoticeOrderingTests
{
    [Fact]
    public async Task A_notice_appended_before_CompleteAsync_gets_a_lower_sequence_than_the_terminal_event()
    {
        var store = new InMemoryRunStore();
        var writer = new RunEventWriter(store, new TraconRunRecordingOptions(), NullLogger.Instance, TraconId.NewId());

        await writer.StartAsync(
            new RunStartInfo { RunId = writer.RunId, AgentName = "test-agent", StartedAt = DateTimeOffset.UtcNow },
            query: "hello");

        var notice = await writer.AppendReservedAsync(
            new RunEventDraft(RunEventType.Custom) { CustomType = RunEventCustomTypes.QuotaThreshold, Payload = "{}" });

        await writer.CompleteAsync(RunStatus.Completed);

        var events = await ReadEventsAsync(store, writer.RunId);
        var terminal = events.Single(static e => e.Type == RunEventType.RunCompleted);

        notice.Sequence.ShouldBeLessThan(terminal.Sequence);
    }

    [Fact]
    public async Task A_reader_from_the_beginning_sees_the_notice_before_the_terminal_event()
    {
        var store = new InMemoryRunStore();
        var writer = new RunEventWriter(store, new TraconRunRecordingOptions(), NullLogger.Instance, TraconId.NewId());

        await writer.StartAsync(
            new RunStartInfo { RunId = writer.RunId, AgentName = "test-agent", StartedAt = DateTimeOffset.UtcNow },
            query: "hello");

        await writer.AppendReservedAsync(
            new RunEventDraft(RunEventType.Custom) { CustomType = RunEventCustomTypes.QuotaThreshold, Payload = "{}" });

        await writer.CompleteAsync(RunStatus.Completed);

        var events = await ReadEventsAsync(store, writer.RunId);
        var noticeIndex = events.FindIndex(static e => e.Type == RunEventType.Custom);
        var terminalIndex = events.FindIndex(static e => e.Type == RunEventType.RunCompleted);

        noticeIndex.ShouldBeGreaterThanOrEqualTo(0);
        noticeIndex.ShouldBeLessThan(terminalIndex);
    }

    [Fact]
    public async Task A_child_run_never_records_quota_consumption()
    {
        // Depth is read directly from TraconRunOptions.Depth
        // (RunRecordingAgent.Lifecycle.PrepareRun) -- the same field
        // ChildAgentInvoker sets for a real sub-agent call. Driving it
        // directly isolates the Depth == 0 gate without standing up the
        // full nested-call machinery AgentDelegationTests.cs already covers.
        using var harness = new QuotaHarness();
        var agent = harness.CreateAgent(new FakeChatClient());

        await agent.RunAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            session: null,
            new TraconRunOptions
            {
                RunId = TraconId.NewId(),
                Depth = 1,
                ParentRunId = TraconId.NewId(),
                RootRunId = TraconId.NewId(),
            });

        var usage = await harness.QuotaStore.GetUsageAsync(new QuotaUsageQuery { TenantId = QuotaHarness.Tenant });
        usage.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_root_run_records_quota_consumption()
    {
        // Control case for the test above: the SAME harness, Depth == 0
        // (the default), DOES record -- proving the child-run test asserts
        // an absence caused by Depth, not by a harness wiring gap.
        using var harness = new QuotaHarness();
        var agent = harness.CreateAgent(new FakeChatClient());

        await agent.RunAsync([new ChatMessage(ChatRole.User, "hello")]);

        var usage = await harness.QuotaStore.GetUsageAsync(new QuotaUsageQuery { TenantId = QuotaHarness.Tenant });
        usage.ShouldNotBeEmpty();
        usage.ShouldAllBe(static record => record.Runs == 1);
    }

    [Fact]
    public async Task A_canceled_run_still_records_quota_consumption()
    {
        // K-162: the quota check happens before a run starts; consumption is
        // recorded when it ends, REGARDLESS of how it ends. A run canceled
        // mid-flight already spent real tokens and must still be counted --
        // 146.1 moved WHEN this write happens (earlier), not WHETHER it does.
        using var harness = new QuotaHarness();
        var agent = harness.CreateAgent(new BlockingChatClient());

        var runTask = agent.RunAsync([new ChatMessage(ChatRole.User, "write a long piece of text")]);

        await WaitUntilAsync(() => harness.Registry.ActiveCount == 1);

        var started = (await harness.Store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        harness.Registry.TryCancel(started.Id, QuotaHarness.Tenant).ShouldBeTrue();

        await Should.ThrowAsync<OperationCanceledException>(() => runTask);

        var run = await harness.Store.GetRunAsync(started.Id);
        run!.Status.ShouldBe(RunStatus.Canceled);

        var usage = await harness.QuotaStore.GetUsageAsync(new QuotaUsageQuery { TenantId = QuotaHarness.Tenant });
        usage.ShouldNotBeEmpty();
        usage.ShouldAllBe(static record => record.Runs == 1);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!condition())
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(10, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task<List<RunEvent>> ReadEventsAsync(InMemoryRunStore store, Guid runId)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in store.ReadEventsAsync(runId, 0, CancellationToken.None))
        {
            events.Add(runEvent);
        }

        return events;
    }

    /// <summary>Wires a real <see cref="QuotaEnforcer"/> into a <see cref="RunRecordingAgent"/>.</summary>
    private sealed class QuotaHarness : IDisposable
    {
        public const string Tenant = "test";

        public QuotaHarness()
        {
            Store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
            Registry = new RunCancellationRegistry();
            QuotaStore = new InMemoryQuotaStore();
            Enforcer = new QuotaEnforcer(QuotaStore, StaticOptions(new TraconQuotaOptions()));
        }

        public InMemoryRunStore Store { get; }

        public RunCancellationRegistry Registry { get; }

        public InMemoryQuotaStore QuotaStore { get; }

        public QuotaEnforcer Enforcer { get; }

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
                cancellationRegistry: Registry,
                quotaEnforcer: Enforcer);
        }

        private static StaticOptionsMonitor<TraconQuotaOptions> StaticOptions(TraconQuotaOptions options) => new(options);

        public void Dispose()
        {
        }

        private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
        {
            public T CurrentValue => value;

            public T Get(string? name) => value;

            public IDisposable? OnChange(Action<T, string?> listener) => null;
        }
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public string TenantId => QuotaHarness.Tenant;
    }
}
