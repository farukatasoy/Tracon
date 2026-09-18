using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// Run recording is best-effort by design: a store failure never interrupts the run.
/// These tests hold the other half of that bargain — the loss is COUNTED, on a closed
/// set of stage tags, exactly once per run.
/// </summary>
/// <remarks>
/// The store-boundary half of this contract lives in the PostgreSQL failure manifest
/// (<c>DatabaseUnavailableTests</c>), against a database that is genuinely down. What is
/// measured here is the writer's own arithmetic, which a real outage cannot isolate:
/// how many times it counts, with which tags, and what it does when there is no tenant.
/// </remarks>
public sealed class RunRecordingFailureMetricTests
{
    private const string Counter = TraconDiagnostics.RunRecordingFailureCounterName;

    [Fact]
    public async Task A_run_that_loses_its_record_is_counted_once_no_matter_how_many_writes_fail()
    {
        using var factory = new TestMeterFactory();
        using var collector = new MetricCollector(factory.Meter);
        using var metrics = new TraconMetrics(factory);

        var store = new FailingRunStore(FailAt.StartRun);
        var writer = NewWriter(store, metrics);

        // Three write attempts are MADE; only the first one reaches the store.
        await writer.StartAsync(Start(writer, "tenant-a"), "the user's question");
        await writer.AppendAsync(new RunEventDraft(RunEventType.MessageDelta) { Text = "delta" });
        await writer.CompleteAsync(RunStatus.Completed);

        // 🚨 That difference IS the mechanism, so it is asserted rather than described:
        // IsDisabled stops the writer from attempting anything after the first failure,
        // which is why one broken run is one measurement however long it runs. The
        // concurrent case is a DIFFERENT mechanism and has its own test below.
        store.StoreCallCount.ShouldBe(1, "a disabled writer must not keep calling the store.");
        writer.IsDisabled.ShouldBeTrue();

        collector.LongValues(Counter).Count.ShouldBe(1);

        var tags = collector.LongTags(Counter).ShouldHaveSingleItem();
        tags[TraconDiagnostics.Tags.RecordingStage].ShouldBe(RunRecordingStages.Start);
        tags[TraconDiagnostics.Tags.TenantId].ShouldBe("tenant-a");
    }

    [Fact]
    public async Task Concurrent_writes_that_fail_together_still_count_the_run_once()
    {
        const int Writers = 8;

        using var factory = new TestMeterFactory();
        using var collector = new MetricCollector(factory.Meter);
        using var metrics = new TraconMetrics(factory);

        // 🚨 The race is forced, not hoped for. Every caller is held inside the store
        // until all eight have arrived, so all eight pass the IsDisabled check before
        // any of them fails. That is the ONE arrangement in which IsDisabled does not
        // provide the once-ness, and it is not hypothetical: a child run writes its
        // summary events into the root run's writer.
        var store = new GatedFailingRunStore(Writers);
        var writer = NewWriter(store, metrics);

        await writer.StartAsync(Start(writer, "tenant-a"), "the user's question");

        await Task.WhenAll(Enumerable.Range(0, Writers).Select(index =>
            Task.Run(async () =>
                await writer.AppendAsync(new RunEventDraft(RunEventType.MessageDelta) { Text = $"delta {index}" }))));

        collector.LongValues(Counter).Count.ShouldBe(
            1,
            "eight writes lost ONE run's record; counting per exception would scale the " +
            "alarm with a run's concurrency rather than with the loss.");

        collector.LongTags(Counter)
            .ShouldHaveSingleItem()[TraconDiagnostics.Tags.RecordingStage]
            .ShouldBe(RunRecordingStages.Event);
    }

    [Fact]
    public async Task A_failure_is_tagged_with_the_stage_it_happened_at()
    {
        using var factory = new TestMeterFactory();
        using var collector = new MetricCollector(factory.Meter);
        using var metrics = new TraconMetrics(factory);

        // The run OPENS; only the event write fails. A start-stage tag here would
        // send an operator to the wrong query.
        var writer = NewWriter(new FailingRunStore(FailAt.AppendEvent), metrics);

        await writer.StartAsync(Start(writer, "tenant-a"), "the user's question");

        collector.LongTags(Counter)
            .ShouldHaveSingleItem()[TraconDiagnostics.Tags.RecordingStage]
            .ShouldBe(RunRecordingStages.Event);
    }

    [Fact]
    public async Task A_lost_tool_invocation_is_counted_at_its_own_stage()
    {
        using var factory = new TestMeterFactory();
        using var collector = new MetricCollector(factory.Meter);
        using var metrics = new TraconMetrics(factory);

        var writer = NewWriter(new FailingRunStore(FailAt.ToolInvocation), metrics);

        await writer.StartAsync(Start(writer, "tenant-a"), "the user's question");
        await writer.RecordToolInvocationAsync(new ToolInvocationRecord
        {
            Id = TraconId.NewId(),
            RunId = writer.RunId,
            ToolName = "get_weather",
            CreatedAt = DateTimeOffset.UtcNow,
            Duration = TimeSpan.FromMilliseconds(12),
        });

        collector.LongTags(Counter)
            .ShouldHaveSingleItem()[TraconDiagnostics.Tags.RecordingStage]
            .ShouldBe(RunRecordingStages.ToolInvocation);
    }

    [Fact]
    public async Task A_lost_closing_write_is_counted_at_the_completion_stage()
    {
        using var factory = new TestMeterFactory();
        using var collector = new MetricCollector(factory.Meter);
        using var metrics = new TraconMetrics(factory);

        var writer = NewWriter(new FailingRunStore(FailAt.CompleteRun), metrics);

        await writer.StartAsync(Start(writer, "tenant-a"), "the user's question");
        await writer.CompleteAsync(RunStatus.Completed);

        collector.LongTags(Counter)
            .ShouldHaveSingleItem()[TraconDiagnostics.Tags.RecordingStage]
            .ShouldBe(RunRecordingStages.Completion);
    }

    [Fact]
    public async Task A_failing_sink_is_counted_at_the_sink_stage_and_never_as_a_store_failure()
    {
        using var factory = new TestMeterFactory();
        using var collector = new MetricCollector(factory.Meter);
        using var metrics = new TraconMetrics(factory);

        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext("tenant-a"));
        var failing = new ThrowingRunEventSink();
        var healthy = new SpyRunEventSink();

        var writer = new RunEventWriter(
            store,
            new TraconRunRecordingOptions(),
            NullLogger.Instance,
            TraconId.NewId(),
            metrics,
            [failing, healthy]);

        await writer.StartAsync(Start(writer, "tenant-a"), "the user's question");
        await writer.AppendAsync(new RunEventDraft(RunEventType.MessageDelta) { Text = "delta" });

        // The store never failed, so the writer is still live and the sink stage is
        // the only thing counted. Conflating the two would make a broken consumer
        // sink look like a broken database.
        writer.IsDisabled.ShouldBeFalse();

        var tags = collector.LongTags(Counter).ShouldHaveSingleItem();
        tags[TraconDiagnostics.Tags.RecordingStage].ShouldBe(RunRecordingStages.Sink);

        // Two events were dispatched but the sink is dropped after the first
        // failure, so the count follows the sink, not the event stream.
        failing.CallCount.ShouldBe(1);

        // And the healthy sink is untouched by its neighbour's failure.
        healthy.Events.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Concurrent_events_into_one_failing_sink_count_that_sink_once()
    {
        const int Events = 8;

        using var factory = new TestMeterFactory();
        using var collector = new MetricCollector(factory.Meter);
        using var metrics = new TraconMetrics(factory);

        // 🚨 The store side got a forced-race test; this is its sink-side twin, and
        // the guarantee is the same shape. The published sentence is "counted once
        // per sink per run", and per-sink concurrency is real: AllowConcurrentToolCalls
        // fans tool events out at once, and a child run writes into the root writer.
        var sink = new GatedThrowingRunEventSink(Events);
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext("tenant-a"));

        var writer = new RunEventWriter(
            store,
            new TraconRunRecordingOptions(),
            NullLogger.Instance,
            TraconId.NewId(),
            metrics,
            [sink]);

        await writer.StartAsync(Start(writer, "tenant-a"), "the user's question");

        await Task.WhenAll(Enumerable.Range(0, Events).Select(index =>
            Task.Run(async () =>
                await writer.AppendAsync(new RunEventDraft(RunEventType.MessageDelta) { Text = $"delta {index}" }))));

        collector.LongValues(Counter).Count.ShouldBe(
            1,
            "one sink broke once; counting per event would make a chatty run look " +
            "like a worse outage than a quiet one with the same broken sink.");

        collector.LongTags(Counter)
            .ShouldHaveSingleItem()[TraconDiagnostics.Tags.RecordingStage]
            .ShouldBe(RunRecordingStages.Sink);
    }

    [Fact]
    public async Task Two_runs_sharing_one_sink_instance_each_count_their_own_failure()
    {
        using var factory = new TestMeterFactory();
        using var collector = new MetricCollector(factory.Meter);
        using var metrics = new TraconMetrics(factory);

        // One sink object, two writers — which is the real topology: sinks are
        // registered once in DI and every concurrent run shares the instance.
        // _sinkDisabled lives on the WRITER, so one run's broken sink must not
        // silence another run's accounting of the same sink.
        var sink = new ThrowingRunEventSink();
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext("tenant-a"));

        foreach (var tenant in new[] { "tenant-a", "tenant-b" })
        {
            var writer = new RunEventWriter(
                store,
                new TraconRunRecordingOptions(),
                NullLogger.Instance,
                TraconId.NewId(),
                metrics,
                [sink]);

            await writer.StartAsync(Start(writer, tenant), "the user's question");
        }

        var tags = collector.LongTags(Counter);

        tags.Count.ShouldBe(2, "each run lost its own sink copy; one run must not absorb the other's.");
        tags.ShouldAllBe(tag => (string)tag[TraconDiagnostics.Tags.RecordingStage]! == RunRecordingStages.Sink);

        tags.Select(tag => (string)tag[TraconDiagnostics.Tags.TenantId]!)
            .OrderBy(tenant => tenant, StringComparer.Ordinal)
            .ShouldBe(["tenant-a", "tenant-b"]);
    }

    [Fact]
    public async Task A_run_without_a_tenant_is_counted_under_a_named_series_not_an_empty_one()
    {
        using var factory = new TestMeterFactory();
        using var collector = new MetricCollector(factory.Meter);
        using var metrics = new TraconMetrics(factory);

        var writer = NewWriter(new FailingRunStore(FailAt.StartRun), metrics);

        await writer.StartAsync(Start(writer, tenantId: null), "the user's question");

        // An exporter does not distinguish an empty tag from a missing one, and an
        // alert rule must not have to. The counter is a source of alarms; a series
        // nobody can name is worse than a named one nobody owns.
        collector.LongTags(Counter)
            .ShouldHaveSingleItem()[TraconDiagnostics.Tags.TenantId]
            .ShouldBe("unknown");
    }

    [Fact]
    public async Task A_cancellation_is_not_a_lost_record_and_is_not_counted()
    {
        using var factory = new TestMeterFactory();
        using var collector = new MetricCollector(factory.Meter);
        using var metrics = new TraconMetrics(factory);

        var writer = NewWriter(new FailingRunStore(FailAt.Cancellation), metrics);

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await writer.StartAsync(Start(writer, "tenant-a"), "the user's question"));

        // A canceled write lost nothing that was going to be written. Counting it
        // would put a spike on the dashboard every time a client hung up.
        collector.LongValues(Counter).ShouldBeEmpty();
        writer.IsDisabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Another_tenants_run_never_lands_on_this_tenants_series()
    {
        using var factory = new TestMeterFactory();
        using var collector = new MetricCollector(factory.Meter);
        using var metrics = new TraconMetrics(factory);

        // The ambient tenant and the run's own tenant deliberately disagree — this is
        // how a workflow or a queued job runs (K-355). The counter must follow the
        // RUN, or an outage would be billed to whichever tenant happened to be
        // ambient on the worker thread.
        var store = new FailingRunStore(FailAt.StartRun);
        var writer = new RunEventWriter(
            store,
            new TraconRunRecordingOptions(),
            NullLogger.Instance,
            TraconId.NewId(),
            metrics);

        await writer.StartAsync(Start(writer, "tenant-owning-the-run"), "the user's question");

        collector.LongTags(Counter)
            .ShouldHaveSingleItem()[TraconDiagnostics.Tags.TenantId]
            .ShouldBe("tenant-owning-the-run");
    }

    [Fact]
    public async Task A_run_through_RunRecordingAgent_counts_its_lost_record()
    {
        using var factory = new TestMeterFactory();
        using var collector = new MetricCollector(factory.Meter);
        using var metrics = new TraconMetrics(factory);

        // 🚨 Every other test on this class builds the writer BY HAND, which proves
        // the writer's arithmetic and nothing about production. The compiler forces
        // the two production call sites to pass SOMETHING; it cannot force them to
        // pass the metrics they hold. Passing null there compiles, ships, and makes
        // this entire phase a no-op for every run that goes through an agent — the
        // same shape as the RunEventWriter.CompleteAsync cost parameter that was
        // added to the signature and never written to the body.
        var client = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider(client)), TestData.Registry());

        var agent = new RunRecordingAgent(
            compiler.Compile(TestData.Definition()),
            new FailingRunStore(FailAt.StartRun),
            new FixedTenantContext("tenant-a"),
            new TraconRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            metrics: metrics);

        // The run answers normally: that is the guarantee this phase does NOT change.
        var response = await agent.RunAsync("hello");
        response.ShouldNotBeNull();

        var tags = collector.LongTags(Counter).ShouldHaveSingleItem();
        tags[TraconDiagnostics.Tags.RecordingStage].ShouldBe(RunRecordingStages.Start);
        tags[TraconDiagnostics.Tags.TenantId].ShouldBe("tenant-a");
    }

    [Fact]
    public async Task A_lost_run_input_is_counted_even_though_the_run_itself_succeeds()
    {
        using var factory = new TestMeterFactory();
        using var collector = new MetricCollector(factory.Meter);
        using var metrics = new TraconMetrics(factory);

        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext("tenant-a"));

        var client = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider(client)), TestData.Registry());

        var agent = new RunRecordingAgent(
            compiler.Compile(TestData.Definition()),
            store,
            new FixedTenantContext("tenant-a"),
            new TraconRunRecordingOptions { RecordRunInput = true },
            NullLogger<RunRecordingAgent>.Instance,
            metrics: metrics,
            runInputStore: new ThrowingRunInputStore());

        // 🚨 This loss is invisible from everywhere else. The run completes, the
        // client gets an answer, and the run record is whole — only the replay
        // input is gone, and nothing about the response says so.
        var response = await agent.RunAsync("hello");
        response.ShouldNotBeNull();

        (await store.QueryRunsAsync(new RunQuery()))
            .ShouldHaveSingleItem()
            .Status.ShouldBe(RunStatus.Completed);

        var tags = collector.LongTags(Counter).ShouldHaveSingleItem();
        tags[TraconDiagnostics.Tags.RecordingStage].ShouldBe(RunRecordingStages.Input);
        tags[TraconDiagnostics.Tags.TenantId].ShouldBe("tenant-a");
    }

    [Fact]
    public void Every_stage_value_has_a_log_sentence_of_its_own()
    {
        // The tag value is the contract and the sentence is the human half of it.
        // A stage added with a constant but no sentence would publish a tag an
        // operator can filter on next to a log line that explains nothing.
        var sentences = RunRecordingStages.All.Select(RunRecordingStages.Describe).ToList();

        sentences.ShouldNotContain(
            sentence => string.Equals(sentence, RunRecordingStages.UnknownDescription, StringComparison.Ordinal));
        sentences.Distinct(StringComparer.Ordinal).Count().ShouldBe(RunRecordingStages.All.Count);
    }

    [Fact]
    public void The_stage_set_is_closed_and_its_values_are_metric_safe()
    {
        string[] expected = ["start", "event", "tool_invocation", "completion", "sink", "input"];

        RunRecordingStages.All.SequenceEqual(expected, StringComparer.Ordinal).ShouldBeTrue(
            $"the stage set is closed; it is [{string.Join(", ", RunRecordingStages.All)}]");

        // Lowercase snake_case, no consumer data: the bound on this tag's
        // cardinality is the length of this list and nothing else.
        RunRecordingStages.All.ShouldAllBe(stage =>
            stage.All(character => char.IsAsciiLetterLower(character) || character == '_'));
    }

    private static RunEventWriter NewWriter(IRunStore store, TraconMetrics metrics)
        => new(store, new TraconRunRecordingOptions(), NullLogger.Instance, TraconId.NewId(), metrics);

    /// <summary>Start info for <paramref name="writer"/>'s own run.</summary>
    /// <remarks>
    /// The run identity must be the writer's: a store that keeps real state rejects
    /// an event for a run it never opened, which would disable the writer for a
    /// reason the test was not asking about.
    /// </remarks>
    private static RunStartInfo Start(RunEventWriter writer, string? tenantId) => new()
    {
        RunId = writer.RunId,
        AgentName = "test-agent",
        StartedAt = DateTimeOffset.UtcNow,
        TenantId = tenantId,
    };

    /// <summary>
    /// Which SINGLE store write the fake refuses. Every other write succeeds — the
    /// name says one write, because the writer disables itself after the first
    /// failure and a fake that claimed to fail "everything" would never be asked.
    /// </summary>
    private enum FailAt
    {
        StartRun,
        AppendEvent,
        ToolInvocation,
        CompleteRun,
        Cancellation,
    }

    /// <summary>
    /// A store that refuses one chosen write. Every other member throws, so a test
    /// that strays off the recording path fails loudly instead of reading a default.
    /// </summary>
    private sealed class FailingRunStore(FailAt failAt) : IRunStore
    {
        private int _storeCalls;

        /// <summary>How many writes actually reached the store.</summary>
        public int StoreCallCount => Volatile.Read(ref _storeCalls);

        public ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _storeCalls);

            if (failAt is FailAt.Cancellation)
            {
                throw new OperationCanceledException();
            }

            return failAt is FailAt.StartRun
                ? throw new InvalidOperationException("store unavailable")
                : ValueTask.FromResult(new RunRecord
                {
                    Id = info.RunId,
                    AgentName = info.AgentName,
                    Status = RunStatus.Running,
                    StartedAt = info.StartedAt,
                    TenantId = info.TenantId,
                });
        }

        public ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _storeCalls);

            return failAt is FailAt.AppendEvent
                ? throw new InvalidOperationException("store unavailable")
                : ValueTask.CompletedTask;
        }

        public ValueTask<long?> GetLastEventSequenceAsync(Guid runId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<long?>(null);

        public ValueTask RecordToolInvocationAsync(ToolInvocationRecord invocation, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _storeCalls);

            return failAt is FailAt.ToolInvocation
                ? throw new InvalidOperationException("store unavailable")
                : ValueTask.CompletedTask;
        }

        public ValueTask<bool> CompleteLateToolInvocationAsync(LateToolCompletion completion, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _storeCalls);

            return failAt is FailAt.ToolInvocation
                ? throw new InvalidOperationException("store unavailable")
                : ValueTask.FromResult(true);
        }

        public ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _storeCalls);

            return failAt is FailAt.CompleteRun
                ? throw new InvalidOperationException("store unavailable")
                : ValueTask.CompletedTask;
        }

        public ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<RunRecord>> QueryRunsAsync(RunQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<RunStatistics> GetStatisticsAsync(RunStatisticsQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<RunEvent> ReadEventsAsync(Guid runId, long fromSequence = 0, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ToolInvocationRecord>> ListToolInvocationsAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ToolUsage>> GetToolUsageAsync(ToolUsageQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ExperimentVariantResult>> GetExperimentResultsAsync(ExperimentResultsQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(RunTimeSeriesQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask UpdateRunCostAsync(Guid runId, RunCost? cost, string? tenantId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask TouchHeartbeatAsync(IReadOnlyCollection<Guid> runIds, DateTimeOffset at, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<RunRecord>> ClaimOrphanedRunsAsync(DateTimeOffset staleBefore, int max, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Opens the run, then holds every event write until a fixed number of callers have
    /// arrived and fails all of them at once.
    /// </summary>
    /// <remarks>
    /// This turns "two writes could race into the same failure" from a timing accident
    /// into a fact of the test. Without it the race is unobservable and the guard it
    /// covers cannot be told apart from dead code.
    /// </remarks>
    private sealed class GatedFailingRunStore(int arrivals) : IRunStore
    {
        private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new RunRecord
            {
                Id = info.RunId,
                AgentName = info.AgentName,
                Status = RunStatus.Running,
                StartedAt = info.StartedAt,
                TenantId = info.TenantId,
            });

        public async ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
        {
            // StartAsync writes RunStarted through this same method, and it must not be
            // held: the gate only counts the concurrent batch the test sends.
            if (runEvent.Type == RunEventType.RunStarted)
            {
                return;
            }

            if (Interlocked.Increment(ref _arrived) == arrivals)
            {
                _allArrived.SetResult();
            }

            await _allArrived.Task.ConfigureAwait(false);

            throw new InvalidOperationException("store unavailable");
        }

        public ValueTask<long?> GetLastEventSequenceAsync(Guid runId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<long?>(null);

        public ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask RecordToolInvocationAsync(ToolInvocationRecord invocation, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<bool> CompleteLateToolInvocationAsync(LateToolCompletion completion, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<RunRecord>> QueryRunsAsync(RunQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<RunStatistics> GetStatisticsAsync(RunStatisticsQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<RunEvent> ReadEventsAsync(Guid runId, long fromSequence = 0, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ToolInvocationRecord>> ListToolInvocationsAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ToolUsage>> GetToolUsageAsync(ToolUsageQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ExperimentVariantResult>> GetExperimentResultsAsync(ExperimentResultsQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(RunTimeSeriesQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask UpdateRunCostAsync(Guid runId, RunCost? cost, string? tenantId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask TouchHeartbeatAsync(IReadOnlyCollection<Guid> runIds, DateTimeOffset at, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<RunRecord>> ClaimOrphanedRunsAsync(DateTimeOffset staleBefore, int max, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>Refuses to store the run input, exactly like a table that is not writable.</summary>
    private sealed class ThrowingRunInputStore : IRunInputStore
    {
        public ValueTask SaveAsync(RunInputRecord record, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("run_inputs is not writable");

        public ValueTask<RunInputRecord?> GetAsync(string tenantId, Guid runId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>Records what it received, so a healthy neighbour can be proven healthy.</summary>
    private sealed class SpyRunEventSink : IRunEventSink
    {
        public List<RunEvent> Events { get; } = [];

        public ValueTask OnEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(runEvent);

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Holds every caller until a fixed number have arrived, then fails all of them
    /// at once — the sink-side twin of <see cref="GatedFailingRunStore"/>.
    /// </summary>
    private sealed class GatedThrowingRunEventSink(int arrivals) : IRunEventSink
    {
        private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public async ValueTask OnEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
        {
            // StartAsync's RunStarted event must not consume a slot in the gate;
            // the gate counts only the concurrent batch the test sends.
            if (runEvent.Type == RunEventType.RunStarted)
            {
                return;
            }

            if (Interlocked.Increment(ref _arrived) == arrivals)
            {
                _allArrived.SetResult();
            }

            await _allArrived.Task.ConfigureAwait(false);

            throw new InvalidOperationException("sink unavailable");
        }
    }

    /// <summary>Throws on every call, and counts how often it was asked.</summary>
    private sealed class ThrowingRunEventSink : IRunEventSink
    {
        public int CallCount { get; private set; }

        public ValueTask OnEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
        {
            CallCount++;

            throw new InvalidOperationException("sink unavailable");
        }
    }
}
