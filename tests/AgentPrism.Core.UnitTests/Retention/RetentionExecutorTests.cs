using AgentPrism.Core.UnitTests.Fakes;

namespace AgentPrism.Core.UnitTests.Retention;

/// <summary>
/// Tests for <see cref="RetentionExecutor"/>'s preview/run orchestration.
/// </summary>
public sealed class RetentionExecutorTests
{
    private const string Tenant = "acme";

    [Fact]
    public async Task Unknown_target_throws_ArgumentException()
    {
        var (executor, _, _) = Build();

        await Should.ThrowAsync<ArgumentException>(async ()
            => await executor.PreviewAsync(Tenant, "not_a_real_table"));
    }

    [Fact]
    public async Task Preview_deletes_no_rows()
    {
        var (executor, store, dataStore) = Build();

        await SavePolicyAsync(store, RetentionTargets.RunEvents, maxAgeDays: 30);
        dataStore.CountToReturn = 42;

        var preview = await executor.PreviewAsync(Tenant, RetentionTargets.RunEvents);

        preview.Count.ShouldBe(1);
        preview[0].Enabled.ShouldBeTrue();
        preview[0].MatchingRows.ShouldBe(42);
        dataStore.DeleteCalls.ShouldBe(0);
    }

    [Fact]
    public async Task A_target_without_a_policy_shows_as_disabled_in_the_preview()
    {
        var (executor, _, _) = Build();

        var preview = await executor.PreviewAsync(Tenant, RetentionTargets.RunEvents);

        preview[0].Enabled.ShouldBeFalse();
        preview[0].MaxAgeDays.ShouldBeNull();
        preview[0].MatchingRows.ShouldBe(0);
    }

    [Fact]
    public async Task When_archiving_is_requested_but_no_sink_is_registered_no_rows_are_deleted()
    {
        var (executor, store, dataStore) = Build(archiveSink: null);

        await SavePolicyAsync(store, RetentionTargets.RunEvents, maxAgeDays: 30, archive: true);
        dataStore.DeleteBatchSizes.Enqueue(100);

        var runs = await executor.RunAsync(Tenant, RetentionTargets.RunEvents);

        runs.Count.ShouldBe(1);
        dataStore.DeleteCalls.ShouldBe(0);
        dataStore.ArchiveReadCalls.ShouldBe(0);

        var history = await store.ListRunsAsync(Tenant, RetentionTargets.RunEvents, 0, 10);
        history[0].CompletedAt.ShouldNotBeNull();
        history[0].Error.ShouldBeNull();
        history[0].DeletedRows.ShouldBe(0);
    }

    [Fact]
    public async Task When_an_archive_sink_is_registered_it_writes_before_deleting()
    {
        var sink = new RecordingArchiveSink();
        var (executor, store, dataStore) = Build(archiveSink: sink);

        await SavePolicyAsync(store, RetentionTargets.RunEvents, maxAgeDays: 30, archive: true);
        dataStore.ArchiveRowsToReturn = [new ArchiveRow { Json = "{}" }];
        dataStore.DeleteBatchSizes.Enqueue(1);
        dataStore.DeleteBatchSizes.Enqueue(0);

        var runs = await executor.RunAsync(Tenant, RetentionTargets.RunEvents);

        sink.WriteCalls.ShouldBe(1);
        runs[0].ArchivedRows.ShouldBe(1);
        runs[0].DeletedRows.ShouldBe(1);
    }

    [Fact]
    public async Task Loop_stops_when_a_batch_returns_smaller_than_the_batch_size()
    {
        var (executor, store, dataStore) = Build(batchSize: 2);

        await SavePolicyAsync(store, RetentionTargets.RunEvents, maxAgeDays: 30);
        dataStore.DeleteBatchSizes.Enqueue(2);
        dataStore.DeleteBatchSizes.Enqueue(2);
        dataStore.DeleteBatchSizes.Enqueue(1);

        var runs = await executor.RunAsync(Tenant, RetentionTargets.RunEvents);

        dataStore.DeleteCalls.ShouldBe(3);
        runs[0].DeletedRows.ShouldBe(5);
    }

    [Fact]
    public async Task A_target_without_a_policy_produces_no_runs()
    {
        var (executor, _, dataStore) = Build();

        var runs = await executor.RunAsync(Tenant, RetentionTargets.RunEvents);

        runs.ShouldBeEmpty();
        dataStore.DeleteCalls.ShouldBe(0);
    }

    /// <summary>
    /// 🚨 Gap closed by Phase 36: a policy with MaxAgeDays EMPTY and only MaxRows
    /// set used to delete zero rows until now.
    /// </summary>
    [Fact]
    public async Task A_policy_with_only_MaxRows_set_deletes_rows()
    {
        var (executor, store, dataStore) = Build();

        await SavePolicyAsync(store, RetentionTargets.RunEvents, maxRows: 100);
        dataStore.RowLimitCutoffToReturn = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        dataStore.DeleteBatchSizes.Enqueue(50);
        dataStore.DeleteBatchSizes.Enqueue(0);

        var runs = await executor.RunAsync(Tenant, RetentionTargets.RunEvents);

        runs[0].DeletedRows.ShouldBe(50);
        dataStore.RowLimitCalls.ShouldBe(1);
        dataStore.LastRowLimitMaxRows.ShouldBe(100);
    }

    [Fact]
    public async Task When_MaxRows_is_below_the_table_count_no_delete_query_runs()
    {
        var (executor, store, dataStore) = Build();

        await SavePolicyAsync(store, RetentionTargets.RunEvents, maxRows: 100);
        dataStore.RowLimitCutoffToReturn = null;

        var runs = await executor.RunAsync(Tenant, RetentionTargets.RunEvents);

        runs.Count.ShouldBe(1);
        runs[0].DeletedRows.ShouldBe(0);
        dataStore.DeleteCalls.ShouldBe(0);
    }

    [Fact]
    public async Task When_both_thresholds_are_set_the_newer_one_wins()
    {
        var (executor, store, dataStore) = Build(
            now: new DateTimeOffset(2026, 8, 6, 0, 0, 0, TimeSpan.Zero));

        // MaxAgeDays=30 -> threshold 2026-07-07. The MaxRows threshold (from the row
        // count) is NEWER (2026-08-01) — it deletes more and MUST WIN.
        await SavePolicyAsync(store, RetentionTargets.RunEvents, maxAgeDays: 30, maxRows: 100);
        dataStore.RowLimitCutoffToReturn = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        dataStore.DeleteBatchSizes.Enqueue(10);

        await executor.RunAsync(Tenant, RetentionTargets.RunEvents);

        dataStore.LastCutoffUsed.ShouldBe(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Preview_computes_the_MaxRows_threshold_the_same_way_as_a_real_run()
    {
        var (executor, store, dataStore) = Build();

        await SavePolicyAsync(store, RetentionTargets.RunEvents, maxRows: 100);
        dataStore.RowLimitCutoffToReturn = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        dataStore.CountToReturn = 50;

        var preview = await executor.PreviewAsync(Tenant, RetentionTargets.RunEvents);

        preview[0].Enabled.ShouldBeTrue();
        preview[0].Cutoff.ShouldBe(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));
        preview[0].MatchingRows.ShouldBe(50);
    }

    private static (RetentionExecutor Executor, IRetentionPolicyStore Store, RecordingRetentionStore DataStore) Build(
        IArchiveSink? archiveSink = null,
        int batchSize = 5000,
        DateTimeOffset? now = null)
    {
        var policyStore = new InMemoryRetentionPolicyStore();
        var dataStore = new RecordingRetentionStore();
        var options = new AgentPrismRetentionOptions { BatchSize = batchSize, BatchDelay = TimeSpan.Zero };
        var resolver = new RetentionPolicyResolver(policyStore, new StaticOptionsMonitor<AgentPrismRetentionOptions>(options));

        var executor = new RetentionExecutor(
            policyStore,
            dataStore,
            resolver,
            new StaticOptionsMonitor<AgentPrismRetentionOptions>(options),
            archiveSink,
            new ManualTimeProvider(now ?? new DateTimeOffset(2026, 8, 5, 3, 0, 0, TimeSpan.Zero)));

        return (executor, policyStore, dataStore);
    }

    private static async Task SavePolicyAsync(
        IRetentionPolicyStore store,
        string target,
        int? maxAgeDays = null,
        long? maxRows = null,
        bool archive = false)
    {
        var now = DateTimeOffset.UtcNow;

        await store.SavePolicyAsync(new RetentionPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            Target = target,
            MaxAgeDays = maxAgeDays,
            MaxRows = maxRows,
            Archive = archive,
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    private sealed class RecordingRetentionStore : IRetentionStore
    {
        public int DeleteCalls { get; private set; }

        public int ArchiveReadCalls { get; private set; }

        public int RowLimitCalls { get; private set; }

        public long CountToReturn { get; set; }

        public DateTimeOffset? RowLimitCutoffToReturn { get; set; }

        public long? LastRowLimitMaxRows { get; private set; }

        public DateTimeOffset? LastCutoffUsed { get; private set; }

        public Queue<int> DeleteBatchSizes { get; } = new();

        public IReadOnlyList<ArchiveRow> ArchiveRowsToReturn { get; set; } = [];

        public ValueTask<long> CountOlderThanAsync(string target, string? tenantId, DateTimeOffset cutoff, CancellationToken cancellationToken = default)
        {
            LastCutoffUsed = cutoff;

            return new ValueTask<long>(CountToReturn);
        }

        public ValueTask<IReadOnlyList<ArchiveRow>> ReadForArchiveAsync(
            string target,
            string? tenantId,
            DateTimeOffset cutoff,
            int batchSize,
            CancellationToken cancellationToken = default)
        {
            ArchiveReadCalls++;

            var rows = ArchiveRowsToReturn;
            ArchiveRowsToReturn = [];

            return new ValueTask<IReadOnlyList<ArchiveRow>>(rows);
        }

        public ValueTask<DateTimeOffset?> FindRowLimitCutoffAsync(
            string target,
            string? tenantId,
            long maxRows,
            CancellationToken cancellationToken = default)
        {
            RowLimitCalls++;
            LastRowLimitMaxRows = maxRows;

            return new ValueTask<DateTimeOffset?>(RowLimitCutoffToReturn);
        }

        public ValueTask<int> DeleteBatchAsync(string target, string? tenantId, DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken = default)
        {
            LastCutoffUsed = cutoff;

            DeleteCalls++;

            return new ValueTask<int>(DeleteBatchSizes.Count > 0 ? DeleteBatchSizes.Dequeue() : 0);
        }
    }

    private sealed class RecordingArchiveSink : IArchiveSink
    {
        public int WriteCalls { get; private set; }

        public ValueTask WriteAsync(
            string target,
            DateTimeOffset partitionDate,
            IReadOnlyList<ArchiveRow> rows,
            CancellationToken cancellationToken = default)
        {
            WriteCalls++;

            return ValueTask.CompletedTask;
        }
    }
}
