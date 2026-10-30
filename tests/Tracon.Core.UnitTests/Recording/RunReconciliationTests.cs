using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// <see cref="RunReconciliationService"/> and <see cref="RunHeartbeatWriter"/>
/// contract.
/// </summary>
public sealed class RunReconciliationTests
{
    [Fact]
    public async Task Reconciliation_returns_immediately_without_any_query_when_disabled()
    {
        // 🚨 Sets up a SchemaReadyGate as if a SQL provider WERE registered
        // (MarkReady is NEVER called); Enabled=false must short-circuit
        // WITHOUT waiting for the gate. If the service hangs, the test times
        // out -- this directly tests the zero-surprise rule's "no query is issued" claim.
        var store = new CountingRunStore(new InMemoryRunStore());
        var gate = new SchemaReadyGate([new SqlPersistenceRegistrationMarker("SQLite")]);
        var options = Options(new RunReconciliationOptions { Enabled = false });

        var service = new RunReconciliationService(
            store,
            new InMemorySingletonLeaseStore(),
            new InMemoryJobStore(),
            new EmptyToolRegistry(),
            options,
            Options(new SingletonExecutionOptions()),
            Options(new TraconRunContinuationOptions()),
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
        var runId = TraconId.NewId();

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
            new InMemoryJobStore(),
            new EmptyToolRegistry(),
            options,
            Options(new SingletonExecutionOptions()),
            Options(new TraconRunContinuationOptions()),
            new SchemaReadyGate([]),
            logger: NullLogger<RunReconciliationService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(
            async () => (await store.GetRunAsync(runId))?.Status == RunStatus.Failed,
            "the orphaned run to close");
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
    public async Task Continuation_store_failure_is_logged_and_the_placeholder_closes_to_Failed()
    {
        var store = new InMemoryRunStore();
        var runId = TraconId.NewId();

        await store.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            SessionId = "session-x",
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
            new ThrowingJobStore(),
            new EmptyToolRegistry(),
            options,
            Options(new SingletonExecutionOptions()),
            Options(new TraconRunContinuationOptions { Enabled = true }),
            new SchemaReadyGate([]),
            logger: NullLogger<RunReconciliationService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(
            async () =>
            {
                var current = await store.QueryRunsAsync(new RunQuery { SessionId = "session-x", OnlyRootRuns = false });
                return current.Count == 2 && current.All(static record => record.Status == RunStatus.Failed);
            },
            "the source run and failed continuation placeholder to close");
        await service.StopAsync(TestContext.Current.CancellationToken);

        // The pass did not crash (StopAsync returned normally, above). The
        // source row is Failed as always; the continuation placeholder
        // EnqueueContinuationAsync opened before the queue write failed must
        // also close to Failed -- otherwise it would sit as Queued forever,
        // invisible to reconciliation (which only scans Running rows).
        var all = await store.QueryRunsAsync(new RunQuery { SessionId = "session-x", OnlyRootRuns = false });

        all.Count.ShouldBe(2);
        all.ShouldAllBe(record => record.Status == RunStatus.Failed);

        var placeholder = all.Single(record => record.Id != runId);
        placeholder.ContinuedFromRunId.ShouldBe(runId);
    }

    [Fact]
    public async Task Reconciliation_runs_on_only_one_of_two_instances()
    {
        var innerStore = new InMemoryRunStore();
        var runId = TraconId.NewId();

        await innerStore.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
        });

        var leaseStore = new InMemorySingletonLeaseStore();
        // 🚨 The lease MUST NOT be able to expire inside the test window:
        // renewal runs at one third of the lease but never faster than
        // SingletonGuard.MinimumRenewInterval, so a 1-second lease used to
        // expire under its live owner and the second instance took over -
        // this assertion then depended on the window staying under a second.
        // SingletonExecutionOptionsValidator rejects that configuration now.
        var singletonOptions = Options(new SingletonExecutionOptions
        {
            Enabled = true,
            LeaseDuration = TimeSpan.FromMinutes(5),
        });

        var options = Options(new RunReconciliationOptions
        {
            Enabled = true,
            ScanInterval = TimeSpan.FromMilliseconds(30),
            OrphanThreshold = TimeSpan.FromMinutes(5),
        });

        var storeA = new CountingRunStore(innerStore);
        var storeB = new CountingRunStore(innerStore);

        var continuationOptions = Options(new TraconRunContinuationOptions());

        var serviceA = new RunReconciliationService(
            storeA, leaseStore, new InMemoryJobStore(), new EmptyToolRegistry(), options, singletonOptions,
            continuationOptions, new SchemaReadyGate([]),
            logger: NullLogger<RunReconciliationService>.Instance);
        var serviceB = new RunReconciliationService(
            storeB, leaseStore, new InMemoryJobStore(), new EmptyToolRegistry(), options, singletonOptions,
            continuationOptions, new SchemaReadyGate([]),
            logger: NullLogger<RunReconciliationService>.Instance);

        await serviceA.StartAsync(TestContext.Current.CancellationToken);
        await serviceB.StartAsync(TestContext.Current.CancellationToken);

        await WaitUntil.TrueAsync(
            () => Task.FromResult(storeA.ClaimCalls > 0 ^ storeB.ClaimCalls > 0),
            "exactly one reconciler to acquire the singleton lease");

        if (storeA.ClaimCalls > 0)
        {
            await serviceB.StopAsync(TestContext.Current.CancellationToken);
            await serviceA.StopAsync(TestContext.Current.CancellationToken);
        }
        else
        {
            await serviceA.StopAsync(TestContext.Current.CancellationToken);
            await serviceB.StopAsync(TestContext.Current.CancellationToken);
        }

        (storeA.ClaimCalls > 0 ^ storeB.ClaimCalls > 0).ShouldBeTrue(
            $"storeA.ClaimCalls={storeA.ClaimCalls}, storeB.ClaimCalls={storeB.ClaimCalls}");
    }

    [Fact]
    public async Task The_same_orphaned_run_is_never_continued_twice_by_two_concurrent_reconcilers()
    {
        // Deliberately WITHOUT SingletonExecutionOptions coordination: both
        // instances scan on every tick, racing on the SAME underlying store.
        // The safety net this proves is ClaimOrphanedRunsAsync's own atomic
        // UPDATE ... WHERE status = Running -- once instance A's claim flips
        // the row to Failed, instance B's claim query no longer matches it,
        // so at most one of the two ever calls EnqueueContinuationAsync for it.
        var runStore = new InMemoryRunStore();
        var jobStore = new InMemoryJobStore();
        var runId = TraconId.NewId();

        await runStore.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            SessionId = "session-race",
        });

        var options = Options(new RunReconciliationOptions
        {
            Enabled = true,
            ScanInterval = TimeSpan.FromMilliseconds(10),
            OrphanThreshold = TimeSpan.FromMinutes(5),
        });

        var continuationOptions = Options(new TraconRunContinuationOptions { Enabled = true });
        var noSingleton = Options(new SingletonExecutionOptions());

        var serviceA = new RunReconciliationService(
            runStore, new InMemorySingletonLeaseStore(), jobStore, new EmptyToolRegistry(), options, noSingleton,
            continuationOptions, new SchemaReadyGate([]),
            logger: NullLogger<RunReconciliationService>.Instance);
        var serviceB = new RunReconciliationService(
            runStore, new InMemorySingletonLeaseStore(), jobStore, new EmptyToolRegistry(), options, noSingleton,
            continuationOptions, new SchemaReadyGate([]),
            logger: NullLogger<RunReconciliationService>.Instance);

        await serviceA.StartAsync(TestContext.Current.CancellationToken);
        await serviceB.StartAsync(TestContext.Current.CancellationToken);

        await WaitUntil.TrueAsync(
            async () =>
            {
                var jobs = await jobStore.QueryAsync(new JobQuery { HandlerKey = JobHandlerKeys.RunContinuation });
                var runs = await runStore.QueryRunsAsync(new RunQuery { SessionId = "session-race", OnlyRootRuns = false });
                return jobs.Count == 1 && runs.Count == 2;
            },
            "one continuation to be enqueued");

        await serviceA.StopAsync(TestContext.Current.CancellationToken);
        await serviceB.StopAsync(TestContext.Current.CancellationToken);

        var continuationJobs = await jobStore.QueryAsync(new JobQuery { HandlerKey = JobHandlerKeys.RunContinuation });
        continuationJobs.Count.ShouldBe(1);

        var allRuns = await runStore.QueryRunsAsync(new RunQuery { SessionId = "session-race", OnlyRootRuns = false });
        allRuns.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Heartbeat_writer_only_marks_runs_active_in_this_process()
    {
        var store = new CountingRunStore(new InMemoryRunStore());
        var registry = new RunCancellationRegistry();

        var runId = TraconId.NewId();
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
        await WaitUntil.TrueAsync(
            () => Task.FromResult(store.TouchCalls > 0),
            "the active run heartbeat to be written");
        await writer.StopAsync(TestContext.Current.CancellationToken);

        // Even though the threshold has passed AFTER the record leaves the
        // registry, reconciliation must not treat the run as orphaned -- the
        // heartbeat was written recently.
        var claimed = await store.ClaimOrphanedRunsAsync(
            staleBefore: DateTimeOffset.UtcNow.AddMinutes(-5),
            max: 10);

        claimed.ShouldBeEmpty();
    }

    // --- Cross-instance run cancellation ---

    [Fact]
    public async Task Heartbeat_writer_cancels_a_registered_run_whose_row_carries_a_request()
    {
        var store = new CountingRunStore(new InMemoryRunStore());
        var registry = new RunCancellationRegistry();
        var runId = await StartTenantRunAsync(store, "tenant-x");
        using var cts = new CancellationTokenSource();
        using var registration = registry.Register(runId, runId, "tenant-x", cts);

        // Another instance wrote the request; this process owns the run and learns of it on its heartbeat.
        (await store.RequestCancellationAsync(runId, "tenant-x", DateTimeOffset.UtcNow)).ShouldBeTrue();

        var writer = CreateWriter(store, registry);

        await writer.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => Task.FromResult(cts.IsCancellationRequested), "the owner to cancel the run");
        await writer.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Heartbeat_writer_leaves_a_run_without_a_request_running()
    {
        var store = new CountingRunStore(new InMemoryRunStore());
        var registry = new RunCancellationRegistry();
        var runId = await StartTenantRunAsync(store, "tenant-x");
        using var cts = new CancellationTokenSource();
        using var registration = registry.Register(runId, runId, "tenant-x", cts);

        var writer = CreateWriter(store, registry);

        await writer.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => Task.FromResult(store.TouchCalls >= 3), "three heartbeat cycles");
        await writer.StopAsync(TestContext.Current.CancellationToken);

        cts.IsCancellationRequested.ShouldBeFalse();
    }

    [Fact]
    public async Task Heartbeat_writer_ignores_a_request_on_a_run_this_process_does_not_own()
    {
        var store = new CountingRunStore(new InMemoryRunStore());
        var registry = new RunCancellationRegistry();
        var mine = await StartTenantRunAsync(store, "tenant-x");
        var theirs = await StartTenantRunAsync(store, "tenant-x");
        using var cts = new CancellationTokenSource();
        using var registration = registry.Register(mine, mine, "tenant-x", cts);

        // The ids come from this process's own ledger: the row of the other run is never read back.
        (await store.RequestCancellationAsync(theirs, "tenant-x", DateTimeOffset.UtcNow)).ShouldBeTrue();

        var writer = CreateWriter(store, registry);

        await writer.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => Task.FromResult(store.TouchCalls >= 3), "three heartbeat cycles");
        await writer.StopAsync(TestContext.Current.CancellationToken);

        cts.IsCancellationRequested.ShouldBeFalse();
        store.RequestsSeen.ShouldBe(0);
    }

    [Fact]
    public async Task A_failed_tick_keeps_the_run_and_reads_the_request_next_tick()
    {
        var store = new CountingRunStore(new InMemoryRunStore(), failures: 2);
        var registry = new RunCancellationRegistry();
        var runId = await StartTenantRunAsync(store, "tenant-x");
        using var cts = new CancellationTokenSource();
        using var registration = registry.Register(runId, runId, "tenant-x", cts);

        (await store.RequestCancellationAsync(runId, "tenant-x", DateTimeOffset.UtcNow)).ShouldBeTrue();

        var writer = CreateWriter(store, registry);

        await writer.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => Task.FromResult(cts.IsCancellationRequested), "the request to be read after the failed cycles");
        await writer.StopAsync(TestContext.Current.CancellationToken);

        // Two cycles failed and the writer went on; the request waited on the row.
        store.FailedTouches.ShouldBe(2);
        store.RequestsSeen.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Heartbeat_writer_keeps_handing_the_request_back_until_the_run_ends()
    {
        var store = new CountingRunStore(new InMemoryRunStore());
        var registry = new RunCancellationRegistry();
        var runId = await StartTenantRunAsync(store, "tenant-x");
        using var cts = new CancellationTokenSource();
        using var registration = registry.Register(runId, runId, "tenant-x", cts);

        (await store.RequestCancellationAsync(runId, "tenant-x", DateTimeOffset.UtcNow)).ShouldBeTrue();

        var writer = CreateWriter(store, registry);

        // A run that reads its token late stays registered; the request stays on the row and comes
        // back every cycle. TryCancel is idempotent, so the repeat breaks nothing.
        await writer.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => Task.FromResult(store.RequestsSeen >= 3), "the request to come back on three cycles");
        await writer.StopAsync(TestContext.Current.CancellationToken);

        cts.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public async Task An_orphaned_run_with_a_cancellation_request_is_closed_Failed_and_not_continued()
    {
        // The owner died before it read the request. The claim closes the row as Failed (the process
        // died). A continuation would run the work the operator asked to stop, so none is opened.
        var runStore = new InMemoryRunStore();
        var jobStore = new InMemoryJobStore();
        var runId = TraconId.NewId();

        await runStore.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            SessionId = "session-cancelled",
        });
        (await runStore.RequestCancellationAsync(runId, "default", DateTimeOffset.UtcNow.AddMinutes(-9))).ShouldBeTrue();

        var service = new RunReconciliationService(
            runStore,
            new InMemorySingletonLeaseStore(),
            jobStore,
            new EmptyToolRegistry(),
            Options(new RunReconciliationOptions
            {
                Enabled = true,
                ScanInterval = TimeSpan.FromMilliseconds(30),
                OrphanThreshold = TimeSpan.FromMinutes(5),
                HeartbeatInterval = TimeSpan.FromSeconds(30),
            }),
            Options(new SingletonExecutionOptions()),
            Options(new TraconRunContinuationOptions { Enabled = true }),
            new SchemaReadyGate([]),
            logger: NullLogger<RunReconciliationService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(
            async () => (await runStore.GetRunAsync(runId))?.Status == RunStatus.Failed,
            "the orphaned run to close");
        await WaitUntil.TrueAsync(
            async () =>
            {
                await foreach (var runEvent in runStore.ReadEventsAsync(runId))
                {
                    if (runEvent.Type == RunEventType.RunContinuationBlocked)
                    {
                        return true;
                    }
                }

                return false;
            },
            "the refusal to be written onto the run");
        await service.StopAsync(TestContext.Current.CancellationToken);

        var session = await runStore.QueryRunsAsync(new RunQuery { SessionId = "session-cancelled", OnlyRootRuns = false });

        session.ShouldHaveSingleItem().Id.ShouldBe(runId);
        (await jobStore.QueryAsync(new JobQuery { TenantId = "default", Take = 10 })).ShouldBeEmpty();
    }

    private static async Task<Guid> StartTenantRunAsync(IRunStore store, string tenantId)
    {
        var runId = TraconId.NewId();

        await store.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = tenantId,
        });

        return runId;
    }

    private static RunHeartbeatWriter CreateWriter(IRunStore store, IRunCancellationRegistry registry)
        => new(
            store,
            registry,
            Options(new RunReconciliationOptions { Enabled = true, HeartbeatInterval = TimeSpan.FromMilliseconds(30) }),
            new SchemaReadyGate([]),
            logger: NullLogger<RunHeartbeatWriter>.Instance);

    private static StaticOptionsMonitor<T> Options<T>(T value) where T : class => new(value);

    /// <summary>Fake <see cref="IToolRegistry"/> carrying no tools.</summary>
    private sealed class EmptyToolRegistry : IToolRegistry
    {
        public IReadOnlyList<ToolDescriptor> List() => [];

        public bool TryGet(
            string name,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Microsoft.Extensions.AI.AIFunctionDeclaration? tool)
        {
            tool = null;
            return false;
        }
    }

    /// <summary>Fake <see cref="IJobStore"/> whose <see cref="EnqueueAsync"/> always fails.</summary>
    private sealed class ThrowingJobStore : IJobStore
    {
        private readonly InMemoryJobStore _inner = new();

        public ValueTask<JobRecord> EnqueueAsync(JobRecord job, IReadOnlyList<string> items, CancellationToken cancellationToken = default)
            => throw new TraconException("simulated queue outage");

        public ValueTask<JobRecord?> LeaseAsync(JobLeaseRequest request, CancellationToken cancellationToken = default)
            => _inner.LeaseAsync(request, cancellationToken);

        public ValueTask RenewLeaseAsync(Guid jobId, string owner, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
            => _inner.RenewLeaseAsync(jobId, owner, leaseDuration, cancellationToken);

        public ValueTask<bool> MarkRunningAsync(Guid jobId, string owner, CancellationToken cancellationToken = default)
            => _inner.MarkRunningAsync(jobId, owner, cancellationToken);

        public ValueTask CompleteAsync(JobCompletion completion, CancellationToken cancellationToken = default)
            => _inner.CompleteAsync(completion, cancellationToken);

        public ValueTask ReleaseForRetryAsync(Guid jobId, string errorMessage, TimeSpan? retryAfter = null, CancellationToken cancellationToken = default)
            => _inner.ReleaseForRetryAsync(jobId, errorMessage, retryAfter, cancellationToken);

        public ValueTask<bool> CancelAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
            => _inner.CancelAsync(tenantId, jobId, cancellationToken);

        public ValueTask<JobRecord?> GetAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
            => _inner.GetAsync(tenantId, jobId, cancellationToken);

        public ValueTask<IReadOnlyList<JobRecord>> QueryAsync(JobQuery query, CancellationToken cancellationToken = default)
            => _inner.QueryAsync(query, cancellationToken);

        public ValueTask<IReadOnlyList<JobItemRecord>> ListItemsAsync(Guid jobId, CancellationToken cancellationToken = default)
            => _inner.ListItemsAsync(jobId, cancellationToken);

        public ValueTask ReportItemAsync(JobItemResult item, CancellationToken cancellationToken = default)
            => _inner.ReportItemAsync(item, cancellationToken);

        public ValueTask<IReadOnlyList<JobQueueDepth>> GetQueueDepthAsync(CancellationToken cancellationToken = default)
            => _inner.GetQueueDepthAsync(cancellationToken);
    }

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
    private sealed class CountingRunStore(IRunStore inner, int failures = 0) : IRunStore
    {
        private int _claimCalls;
        private int _touchCalls;
        private int _failedTouches;
        private int _requestsSeen;

        public int ClaimCalls => Volatile.Read(ref _claimCalls);

        /// <summary>Gets the heartbeat calls that reached the inner store and returned.</summary>
        public int TouchCalls => Volatile.Read(ref _touchCalls);

        /// <summary>Gets the heartbeat calls this wrapper failed on purpose (the first <c>failures</c> of them).</summary>
        public int FailedTouches => Volatile.Read(ref _failedTouches);

        /// <summary>Gets how many cancellation requests the heartbeat calls handed back in all.</summary>
        public int RequestsSeen => Volatile.Read(ref _requestsSeen);

        public ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default)
            => inner.StartRunAsync(info, cancellationToken);

        public ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
            => inner.AppendEventAsync(runEvent, cancellationToken);

        public ValueTask<long?> GetLastEventSequenceAsync(Guid runId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<long?>(null);

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

        public ValueTask<bool> CompleteLateToolInvocationAsync(LateToolCompletion completion, CancellationToken cancellationToken = default)
            => inner.CompleteLateToolInvocationAsync(completion, cancellationToken);

        public ValueTask<IReadOnlyList<ToolInvocationRecord>> ListToolInvocationsAsync(Guid runId, CancellationToken cancellationToken = default)
            => inner.ListToolInvocationsAsync(runId, cancellationToken);

        public ValueTask RecordNonTokenUsageAsync(NonTokenUsageRecord record, CancellationToken cancellationToken = default) => inner.RecordNonTokenUsageAsync(record, cancellationToken);

        public ValueTask<IReadOnlyList<NonTokenUsageRecord>> ListNonTokenUsageAsync(Guid runId, CancellationToken cancellationToken = default) => inner.ListNonTokenUsageAsync(runId, cancellationToken);

        public ValueTask<IReadOnlyList<ToolUsage>> GetToolUsageAsync(ToolUsageQuery query, CancellationToken cancellationToken = default)
            => inner.GetToolUsageAsync(query, cancellationToken);

        public ValueTask<IReadOnlyList<ExperimentVariantResult>> GetExperimentResultsAsync(ExperimentResultsQuery query, CancellationToken cancellationToken = default)
            => inner.GetExperimentResultsAsync(query, cancellationToken);

        public ValueTask<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(RunTimeSeriesQuery query, CancellationToken cancellationToken = default)
            => inner.GetTimeSeriesAsync(query, cancellationToken);

        public ValueTask UpdateRunCostAsync(
            Guid runId,
            RunCost? cost,
            string? tenantId,
            CancellationToken cancellationToken = default)
            => inner.UpdateRunCostAsync(runId, cost, tenantId, cancellationToken);

        public async ValueTask<IReadOnlyList<RunCancellationRequest>> TouchHeartbeatAsync(
            IReadOnlyCollection<Guid> runIds,
            DateTimeOffset at,
            CancellationToken cancellationToken = default)
        {
            if (FailedTouches < failures)
            {
                Interlocked.Increment(ref _failedTouches);

                throw new InvalidOperationException("the run store blinked");
            }

            var requests = await inner.TouchHeartbeatAsync(runIds, at, cancellationToken).ConfigureAwait(false);
            Interlocked.Add(ref _requestsSeen, requests.Count);
            Interlocked.Increment(ref _touchCalls);

            return requests;
        }

        public ValueTask<bool> RequestCancellationAsync(
            Guid runId,
            string tenantId,
            DateTimeOffset at,
            CancellationToken cancellationToken = default)
            => inner.RequestCancellationAsync(runId, tenantId, at, cancellationToken);

        public async ValueTask<IReadOnlyList<RunRecord>> ClaimOrphanedRunsAsync(
            DateTimeOffset staleBefore,
            int max,
            CancellationToken cancellationToken = default)
        {
            var claimed = await inner.ClaimOrphanedRunsAsync(staleBefore, max, cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _claimCalls);
            return claimed;
        }
    }
}
