using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Retention;

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
        dataStore.ArchiveBatchCalls.ShouldBe(0);

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

        var runs = await executor.RunAsync(Tenant, RetentionTargets.RunEvents);

        sink.WriteCalls.ShouldBe(1);
        runs[0].ArchivedRows.ShouldBe(1);
        runs[0].DeletedRows.ShouldBe(1);
    }

    /// <summary>
    /// Archive mode read one batch in time order and then deleted
    /// another batch in no order, so rows that were never archived were
    /// deleted. It now deletes only through the store's archive batch, which
    /// deletes the rows it handed to the sink — never through a plain batch delete.
    /// </summary>
    [Fact]
    public async Task Archive_mode_deletes_only_the_rows_the_sink_received()
    {
        var sink = new RecordingArchiveSink();
        var (executor, store, dataStore) = Build(archiveSink: sink, batchSize: 2);

        await SavePolicyAsync(store, RetentionTargets.RunEvents, maxAgeDays: 30, archive: true);
        dataStore.ArchiveBatches.Enqueue([new ArchiveRow { Json = "{\"seq\":1}" }, new ArchiveRow { Json = "{\"seq\":2}" }]);
        dataStore.ArchiveBatches.Enqueue([new ArchiveRow { Json = "{\"seq\":3}" }]);

        var runs = await executor.RunAsync(Tenant, RetentionTargets.RunEvents);

        dataStore.DeleteCalls.ShouldBe(0);
        dataStore.ArchiveReadCalls.ShouldBe(0);
        dataStore.ArchiveBatchCalls.ShouldBe(2);
        sink.Rows.Select(static row => row.Json).ShouldBe(["{\"seq\":1}", "{\"seq\":2}", "{\"seq\":3}"]);
        runs[0].ArchivedRows.ShouldBe(3);
        runs[0].DeletedRows.ShouldBe(3);
    }

    [Fact]
    public async Task A_failed_archive_write_deletes_nothing_and_fails_the_run()
    {
        var sink = new RecordingArchiveSink { Failure = new InvalidOperationException("The archive is not reachable.") };
        var (executor, store, dataStore) = Build(archiveSink: sink);

        await SavePolicyAsync(store, RetentionTargets.RunEvents, maxAgeDays: 30, archive: true);
        dataStore.ArchiveRowsToReturn = [new ArchiveRow { Json = "{}" }];

        await Should.ThrowAsync<InvalidOperationException>(async ()
            => await executor.RunAsync(Tenant, RetentionTargets.RunEvents));

        dataStore.DeleteCalls.ShouldBe(0);
        dataStore.DeletedByArchive.ShouldBe(0);

        var history = await store.ListRunsAsync(Tenant, RetentionTargets.RunEvents, 0, 10);
        history[0].Error.ShouldNotBeNull();
        history[0].DeletedRows.ShouldBe(0);
        history[0].ArchivedRows.ShouldBe(0);
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
    /// 🚨 Gap closed: a policy with MaxAgeDays EMPTY and only MaxRows
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

    /// <summary>
    /// A sweep over every target stopped at the first target that failed, so the targets after it were never
    /// swept in that pass and the job that retried the sweep failed on the same target again.
    /// </summary>
    [Fact]
    public async Task A_target_that_fails_does_not_stop_the_targets_after_it_and_its_failure_is_raised_at_the_end()
    {
        var (executor, store, dataStore) = Build();

        await SavePolicyAsync(store, RetentionTargets.RunEvents, maxAgeDays: 30);
        await SavePolicyAsync(store, RetentionTargets.Traces, maxAgeDays: 30);
        await SavePolicyAsync(store, RetentionTargets.Jobs, maxAgeDays: 30);
        dataStore.FailingTargets[RetentionTargets.RunEvents] = new InvalidOperationException("The first target fails (probe).");
        dataStore.DeleteBatchSizes.Enqueue(3);
        dataStore.DeleteBatchSizes.Enqueue(4);

        var failure = await Should.ThrowAsync<InvalidOperationException>(
            async () => await executor.RunAsync(Tenant, target: null));

        failure.Message.ShouldBe("The first target fails (probe).", "a single failure is raised as it was, so the job keeps its message and type");
        dataStore.TargetsSwept.ShouldBe([RetentionTargets.RunEvents, RetentionTargets.Traces, RetentionTargets.Jobs]);

        (await store.ListRunsAsync(Tenant, RetentionTargets.Traces, 0, 10)).Single().DeletedRows.ShouldBe(3);
        (await store.ListRunsAsync(Tenant, RetentionTargets.Jobs, 0, 10)).Single().DeletedRows.ShouldBe(4);
        (await store.ListRunsAsync(Tenant, RetentionTargets.RunEvents, 0, 10)).Single().Error.ShouldNotBeNull("the failed target's own run record says it failed");
    }

    [Fact]
    public async Task Several_failed_targets_are_raised_together_and_the_others_still_run()
    {
        var (executor, store, dataStore) = Build();

        await SavePolicyAsync(store, RetentionTargets.RunEvents, maxAgeDays: 30);
        await SavePolicyAsync(store, RetentionTargets.Traces, maxAgeDays: 30);
        await SavePolicyAsync(store, RetentionTargets.Jobs, maxAgeDays: 30);
        dataStore.FailingTargets[RetentionTargets.RunEvents] = new InvalidOperationException("one (probe)");
        dataStore.FailingTargets[RetentionTargets.Jobs] = new TimeoutException("two (probe)");
        dataStore.DeleteBatchSizes.Enqueue(5);

        var failure = await Should.ThrowAsync<AggregateException>(
            async () => await executor.RunAsync(Tenant, target: null));

        failure.InnerExceptions.Select(static inner => inner.GetType()).ShouldBe([typeof(InvalidOperationException), typeof(TimeoutException)]);
        failure.Message.ShouldContain(RetentionTargets.RunEvents);
        failure.Message.ShouldContain(RetentionTargets.Jobs);
        (await store.ListRunsAsync(Tenant, RetentionTargets.Traces, 0, 10)).Single().DeletedRows.ShouldBe(5);
    }

    [Fact]
    public async Task A_stop_ends_the_sweep_at_once_and_is_not_counted_as_a_target_failure()
    {
        var (executor, store, dataStore) = Build();
        using var cancellation = new CancellationTokenSource();

        await SavePolicyAsync(store, RetentionTargets.RunEvents, maxAgeDays: 30);
        await SavePolicyAsync(store, RetentionTargets.Traces, maxAgeDays: 30);
        dataStore.OnDelete = _ => cancellation.Cancel();
        dataStore.DeleteBatchSizes.Enqueue(1);

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await executor.RunAsync(Tenant, target: null, cancellation.Token));

        dataStore.TargetsSwept.ShouldBe([RetentionTargets.RunEvents], "the sweep stopped with the stop it was given");
    }

    [Fact]
    public async Task The_job_handler_fails_its_item_and_raises_the_failure_only_after_every_target_was_swept()
    {
        // A job that failed is retried as a whole, so the handler has to raise the failure; what changed is
        // that the targets after the failed one were swept in this pass already.
        var (executor, store, dataStore) = Build();
        var reported = new List<JobItemResult>();
        var jobId = Guid.NewGuid();
        var handler = new RetentionJobHandler(executor);

        await SavePolicyAsync(store, RetentionTargets.RunEvents, maxAgeDays: 30);
        await SavePolicyAsync(store, RetentionTargets.Traces, maxAgeDays: 30);
        dataStore.FailingTargets[RetentionTargets.RunEvents] = new InvalidOperationException("The first target fails (probe).");
        dataStore.DeleteBatchSizes.Enqueue(2);

        var context = new JobContext
        {
            Job = new JobRecord
            {
                Id = jobId,
                TenantId = Tenant,
                HandlerKey = JobHandlerKeys.Retention,
                TargetName = "*",
                Status = JobStatus.Running,
                ScheduledFor = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            Items = [new JobItemRecord { Id = Guid.NewGuid(), JobId = jobId, Seq = 0, Input = string.Empty, Status = JobItemStatus.Pending }],
            ReportItemAsync = (result, _) =>
            {
                reported.Add(result);

                return default;
            },
            IsCancelledAsync = static _ => new ValueTask<bool>(false),
        };

        await Should.ThrowAsync<InvalidOperationException>(async () => await handler.ExecuteAsync(context));

        reported.ShouldHaveSingleItem().Status.ShouldBe(JobItemStatus.Failed);
        (await store.ListRunsAsync(Tenant, RetentionTargets.Traces, 0, 10)).Single().DeletedRows.ShouldBe(2);
    }

    /// <summary>
    /// A policy that keeps rows for longer than the calendar has days keeps everything: nothing is older than it.
    /// </summary>
    /// <remarks>
    /// The cutoff was <c>now - TimeSpan.FromDays(days)</c>, which throws once the result lies before the first instant a
    /// date can hold, and a TimeSpan cannot hold 2 147 483 647 days at all. The API takes any number of days from 1 up, so a
    /// saved policy made the preview answer <c>500</c> and every run of that target fail, for ever.
    /// </remarks>
    [Theory]
    [InlineData(739_000)]
    [InlineData(739_500)]
    [InlineData(10_675_199)]
    [InlineData(10_675_200)]
    [InlineData(int.MaxValue)]
    public async Task A_policy_of_more_days_than_the_calendar_has_keeps_everything(int days)
    {
        var now = new DateTimeOffset(2026, 8, 6, 0, 0, 0, TimeSpan.Zero);
        var (executor, store, dataStore) = Build(now: now);

        await SavePolicyAsync(store, RetentionTargets.RunEvents, maxAgeDays: days);

        var preview = await executor.PreviewAsync(Tenant, RetentionTargets.RunEvents);

        var cutoff = preview[0].Cutoff.ShouldNotBeNull();
        cutoff.ShouldBeLessThan(now);
        dataStore.LastCutoffUsed.ShouldBe(cutoff);

        var runs = await executor.RunAsync(Tenant, RetentionTargets.RunEvents);

        runs.ShouldHaveSingleItem().Error.ShouldBeNull();
        dataStore.LastCutoffUsed.ShouldBe(cutoff);
    }

    [Fact]
    public async Task A_policy_of_exactly_the_days_back_to_the_first_instant_cuts_off_at_that_instant()
    {
        var now = new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);
        var daysToTheFirstInstant = (int)(now.UtcTicks / TimeSpan.TicksPerDay);
        var (executor, store, dataStore) = Build(now: now);

        await SavePolicyAsync(store, RetentionTargets.RunEvents, maxAgeDays: daysToTheFirstInstant);

        var preview = await executor.PreviewAsync(Tenant, RetentionTargets.RunEvents);

        // The last whole day that fits: its cutoff is noon on January 1 of year 1, not the first instant.
        preview[0].Cutoff.ShouldBe(new DateTimeOffset(1, 1, 1, 12, 0, 0, TimeSpan.Zero));

        await SavePolicyAsync(store, RetentionTargets.RunEvents, maxAgeDays: daysToTheFirstInstant + 1);

        (await executor.PreviewAsync(Tenant, RetentionTargets.RunEvents))[0].Cutoff.ShouldBe(DateTimeOffset.MinValue);
        dataStore.DeleteCalls.ShouldBe(0);
    }

    private static (RetentionExecutor Executor, IRetentionPolicyStore Store, RecordingRetentionStore DataStore) Build(
        IArchiveSink? archiveSink = null,
        int batchSize = 5000,
        DateTimeOffset? now = null)
    {
        var policyStore = new InMemoryRetentionPolicyStore();
        var dataStore = new RecordingRetentionStore();
        var options = new TraconRetentionOptions { BatchSize = batchSize, BatchDelay = TimeSpan.Zero };
        var resolver = new RetentionPolicyResolver(policyStore, new StaticOptionsMonitor<TraconRetentionOptions>(options));

        var executor = new RetentionExecutor(
            policyStore,
            dataStore,
            resolver,
            new StaticOptionsMonitor<TraconRetentionOptions>(options),
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

        public int ArchiveBatchCalls { get; private set; }

        /// <summary>The rows the archive batches deleted: only rows whose archive write returned.</summary>
        public int DeletedByArchive { get; private set; }

        /// <summary>The batches <see cref="ArchiveBatchAsync"/> hands out, one per call.</summary>
        public Queue<IReadOnlyList<ArchiveRow>> ArchiveBatches { get; } = new();

        public int RowLimitCalls { get; private set; }

        public long CountToReturn { get; set; }

        public DateTimeOffset? RowLimitCutoffToReturn { get; set; }

        public long? LastRowLimitMaxRows { get; private set; }

        public DateTimeOffset? LastCutoffUsed { get; private set; }

        public Queue<int> DeleteBatchSizes { get; } = new();

        /// <summary>The targets whose delete fails with the given exception.</summary>
        public Dictionary<string, Exception> FailingTargets { get; } = new(StringComparer.Ordinal);

        /// <summary>The targets <see cref="DeleteBatchAsync"/> was called for, in order, once each.</summary>
        public List<string> TargetsSwept { get; } = [];

        /// <summary>A step that runs inside every delete call.</summary>
        public Action<string>? OnDelete { get; set; }

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

            if (!TargetsSwept.Contains(target))
            {
                TargetsSwept.Add(target);
            }

            OnDelete?.Invoke(target);

            if (FailingTargets.TryGetValue(target, out var failure))
            {
                throw failure;
            }

            return new ValueTask<int>(DeleteBatchSizes.Count > 0 ? DeleteBatchSizes.Dequeue() : 0);
        }

        /// <summary>The store contract in miniature: it deletes exactly the rows it archived, after the write returns.</summary>
        public async ValueTask<int> ArchiveBatchAsync(
            string target,
            string? tenantId,
            DateTimeOffset cutoff,
            int batchSize,
            Func<IReadOnlyList<ArchiveRow>, CancellationToken, ValueTask> archiveAsync,
            CancellationToken cancellationToken = default)
        {
            LastCutoffUsed = cutoff;
            ArchiveBatchCalls++;

            IReadOnlyList<ArchiveRow> rows;

            if (ArchiveBatches.Count > 0)
            {
                rows = ArchiveBatches.Dequeue();
            }
            else
            {
                rows = ArchiveRowsToReturn;
                ArchiveRowsToReturn = [];
            }

            if (rows.Count == 0)
            {
                return 0;
            }

            await archiveAsync(rows, cancellationToken);

            DeletedByArchive += rows.Count;

            return rows.Count;
        }
    }

    private sealed class RecordingArchiveSink : IArchiveSink
    {
        public int WriteCalls { get; private set; }

        public List<ArchiveRow> Rows { get; } = [];

        public Exception? Failure { get; init; }

        public ValueTask WriteAsync(
            string target,
            DateTimeOffset partitionDate,
            IReadOnlyList<ArchiveRow> rows,
            CancellationToken cancellationToken = default)
        {
            WriteCalls++;

            if (Failure is not null)
            {
                throw Failure;
            }

            Rows.AddRange(rows);

            return ValueTask.CompletedTask;
        }
    }
}
