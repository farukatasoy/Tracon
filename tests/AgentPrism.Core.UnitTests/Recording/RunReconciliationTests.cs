using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Recording;

/// <summary>
/// <see cref="RunReconciliationService"/> and <see cref="RunHeartbeatWriter"/>
/// contract (Phase 54).
/// </summary>
public sealed class RunReconciliationTests
{
    [Fact]
    public async Task Reconciliation_returns_immediately_without_any_query_when_disabled()
    {
        // 🚨 Sets up a SchemaReadyGate as if a SQL provider WERE registered
        // (MarkReady is NEVER called); Enabled=false must short-circuit
        // WITHOUT waiting for the gate. If the service hangs, the test times
        // out -- this directly tests K1's "no query is issued" claim.
        var store = new CountingRunStore(new InMemoryRunStore());
        var gate = new SchemaReadyGate([new SqlPersistenceRegistrationMarker("SQLite")]);
        var options = Options(new RunReconciliationOptions { Enabled = false });

        var service = new RunReconciliationService(
            store,
            new InMemorySingletonLeaseStore(),
            options,
            Options(new SingletonExecutionOptions()),
            gate,
            logger: NullLogger<RunReconciliationService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        store.ClaimCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Heartbeat_writer_returns_immediately_without_any_query_when_disabled()
    {
        var store = new CountingRunStore(new InMemoryRunStore());
        var registry = new RunCancellationRegistry();
        var gate = new SchemaReadyGate([new SqlPersistenceRegistrationMarker("SQLite")]);
        var options = Options(new RunReconciliationOptions { Enabled = false });

        var writer = new RunHeartbeatWriter(
            store,
            registry,
            options,
            gate,
            logger: NullLogger<RunHeartbeatWriter>.Instance);

        await writer.StartAsync(TestContext.Current.CancellationToken);
        await writer.StopAsync(TestContext.Current.CancellationToken);

        store.TouchCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Run_exceeding_threshold_is_closed_and_ErrorRate_denominator_is_corrected()
    {
        var store = new InMemoryRunStore();
        var runId = AgentPrismId.NewId();

        await store.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
        });

        var options = Options(new RunReconciliationOptions
        {
            Enabled = true,
            ScanInterval = TimeSpan.FromMilliseconds(30),
            OrphanThreshold = TimeSpan.FromMinutes(5),
            HeartbeatInterval = TimeSpan.FromSeconds(30),
        });

        var service = new RunReconciliationService(
            store,
            new InMemorySingletonLeaseStore(),
            options,
            Options(new SingletonExecutionOptions()),
            new SchemaReadyGate([]),
            logger: NullLogger<RunReconciliationService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        var record = await store.GetRunAsync(runId);
        record!.Status.ShouldBe(RunStatus.Failed);
        record.Error!.Class.ShouldBe(RunErrorClass.Infrastructure);

        // settled = CompletedRuns + FailedRuns + CanceledRuns; a Running row
        // never entered the denominator and artificially diluted the error rate.
        var stats = await store.GetStatisticsAsync(new RunStatisticsQuery());
        stats.RunningRuns.ShouldBe(0);
        stats.FailedRuns.ShouldBe(1);
    }

    [Fact]
    public async Task Reconciliation_runs_on_only_one_of_two_instances()
    {
        var innerStore = new InMemoryRunStore();
        var runId = AgentPrismId.NewId();

        await innerStore.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
        });

        var leaseStore = new InMemorySingletonLeaseStore();
        var singletonOptions = Options(new SingletonExecutionOptions
        {
            Enabled = true,
            LeaseDuration = TimeSpan.FromSeconds(1),
        });

        var options = Options(new RunReconciliationOptions
        {
            Enabled = true,
            ScanInterval = TimeSpan.FromMilliseconds(30),
            OrphanThreshold = TimeSpan.FromMinutes(5),
        });

        var storeA = new CountingRunStore(innerStore);
        var storeB = new CountingRunStore(innerStore);

        var serviceA = new RunReconciliationService(
            storeA, leaseStore, options, singletonOptions, new SchemaReadyGate([]),
            logger: NullLogger<RunReconciliationService>.Instance);
        var serviceB = new RunReconciliationService(
            storeB, leaseStore, options, singletonOptions, new SchemaReadyGate([]),
            logger: NullLogger<RunReconciliationService>.Instance);

        await serviceA.StartAsync(TestContext.Current.CancellationToken);
        await serviceB.StartAsync(TestContext.Current.CancellationToken);

        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        await serviceA.StopAsync(TestContext.Current.CancellationToken);
        await serviceB.StopAsync(TestContext.Current.CancellationToken);

        (storeA.ClaimCalls > 0 ^ storeB.ClaimCalls > 0).ShouldBeTrue(
            $"storeA.ClaimCalls={storeA.ClaimCalls}, storeB.ClaimCalls={storeB.ClaimCalls}");
    }

    [Fact]
    public async Task Heartbeat_writer_only_marks_runs_active_in_this_process()
    {
        var store = new InMemoryRunStore();
        var registry = new RunCancellationRegistry();

        var runId = AgentPrismId.NewId();
        await store.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
        });

        using var cts = new CancellationTokenSource();
        using var registration = registry.Register(runId, runId, tenantId: null, cts);

        var options = Options(new RunReconciliationOptions
        {
            Enabled = true,
            HeartbeatInterval = TimeSpan.FromMilliseconds(30),
        });

        var writer = new RunHeartbeatWriter(
            store, registry, options, new SchemaReadyGate([]),
            logger: NullLogger<RunHeartbeatWriter>.Instance);

        await writer.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
        await writer.StopAsync(TestContext.Current.CancellationToken);

        // Even though the threshold has passed AFTER the record leaves the
        // registry, reconciliation must not treat the run as orphaned -- the
        // heartbeat was written recently.
        var claimed = await store.ClaimOrphanedRunsAsync(
            staleBefore: DateTimeOffset.UtcNow.AddMinutes(-5),
            max: 10);

        claimed.ShouldBeEmpty();
    }

    private static StaticOptionsMonitor<T> Options<T>(T value) where T : class => new(value);

    /// <summary>Fake <see cref="IOptionsMonitor{T}"/> that returns a fixed value and never watches for changes.</summary>
    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
        where T : class
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    /// <summary>
    /// Wrapper that tracks call counts for <see cref="IRunStore.ClaimOrphanedRunsAsync"/>
    /// and <see cref="IRunStore.TouchHeartbeatAsync"/>, delegating everything else to the inner store.
    /// </summary>
    private sealed class CountingRunStore(IRunStore inner) : IRunStore
    {
        public int ClaimCalls { get; private set; }

        public int TouchCalls { get; private set; }

        public ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default)
            => inner.StartRunAsync(info, cancellationToken);

        public ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
            => inner.AppendEventAsync(runEvent, cancellationToken);

        public ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken cancellationToken = default)
            => inner.CompleteRunAsync(completion, cancellationToken);

        public ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
            => inner.GetRunAsync(runId, cancellationToken);

        public ValueTask<IReadOnlyList<RunRecord>> QueryRunsAsync(RunQuery query, CancellationToken cancellationToken = default)
            => inner.QueryRunsAsync(query, cancellationToken);

        public ValueTask<RunStatistics> GetStatisticsAsync(RunStatisticsQuery query, CancellationToken cancellationToken = default)
            => inner.GetStatisticsAsync(query, cancellationToken);

        public IAsyncEnumerable<RunEvent> ReadEventsAsync(Guid runId, long fromSequence = 0, CancellationToken cancellationToken = default)
            => inner.ReadEventsAsync(runId, fromSequence, cancellationToken);

        public ValueTask RecordToolInvocationAsync(ToolInvocationRecord invocation, CancellationToken cancellationToken = default)
            => inner.RecordToolInvocationAsync(invocation, cancellationToken);

        public ValueTask<IReadOnlyList<ToolInvocationRecord>> ListToolInvocationsAsync(Guid runId, CancellationToken cancellationToken = default)
            => inner.ListToolInvocationsAsync(runId, cancellationToken);

        public ValueTask<IReadOnlyList<ToolUsage>> GetToolUsageAsync(ToolUsageQuery query, CancellationToken cancellationToken = default)
            => inner.GetToolUsageAsync(query, cancellationToken);

        public ValueTask<IReadOnlyList<ExperimentVariantResult>> GetExperimentResultsAsync(ExperimentResultsQuery query, CancellationToken cancellationToken = default)
            => inner.GetExperimentResultsAsync(query, cancellationToken);

        public ValueTask<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(RunTimeSeriesQuery query, CancellationToken cancellationToken = default)
            => inner.GetTimeSeriesAsync(query, cancellationToken);

        public ValueTask UpdateRunCostAsync(
            Guid runId,
            RunCost? cost,
            string? tenantId = null,
            CancellationToken cancellationToken = default)
            => inner.UpdateRunCostAsync(runId, cost, tenantId, cancellationToken);

        public ValueTask TouchHeartbeatAsync(
            IReadOnlyCollection<Guid> runIds,
            DateTimeOffset at,
            CancellationToken cancellationToken = default)
        {
            TouchCalls++;
            return inner.TouchHeartbeatAsync(runIds, at, cancellationToken);
        }

        public ValueTask<IReadOnlyList<RunRecord>> ClaimOrphanedRunsAsync(
            DateTimeOffset staleBefore,
            int max,
            CancellationToken cancellationToken = default)
        {
            ClaimCalls++;
            return inner.ClaimOrphanedRunsAsync(staleBefore, max, cancellationToken);
        }
    }
}
