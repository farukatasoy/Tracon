using Tracon.Testing.Contracts.Internal;

namespace Tracon.Testing.Contracts.Storage;

/// <summary>
/// Behavior tests for the <see cref="IJobStore"/> contract.
/// </summary>
/// <remarks>
/// The in-memory store and the SQL stores must pass the same
/// scenarios. The lease-expiry test runs against real time (a short lease +
/// a short wait): the contract gets its store from <c>CreateStoreAsync</c> and
/// has no way to hand it a clock, so fake time is not used here. Only the
/// in-memory and SQLite stores take their clock through a <see cref="TimeProvider"/>;
/// PostgreSQL and SQL Server read the database server's own clock inside the lease
/// statements and ignore one. A test that needs two nodes with two clocks builds the
/// stores itself.
/// </remarks>
public abstract class JobStoreContract : TenantIsolationContract<IJobStore>
{
    /// <summary>How many times a pair of writers is released at one job: one race is a chance, forty are a measurement.</summary>
    private const int ConcurrentRounds = 150;

    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name, CancellationToken cancellationToken)
        => (await Store.EnqueueAsync(TestData.Job(tenantId), [name], cancellationToken)).Id;

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
        => await Store.GetAsync(tenantId, (Guid)key) is not null;

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId, CancellationToken cancellationToken)
        => (await Store.QueryAsync(new JobQuery { TenantId = tenantId }, cancellationToken)).Count;

    /// <inheritdoc />
    /// <remarks>
    /// There is no delete in the job queue; the only destructive operation
    /// available to a tenant is cancel.
    /// </remarks>
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.CancelAsync(tenantId, (Guid)key);

    [Fact]
    public async Task Enqueued_job_starts_pending_and_creates_items()
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a", "b", "c"]);

        job.Status.ShouldBe(JobStatus.Pending);
        job.TotalItems.ShouldBe(3);
        job.Attempt.ShouldBe(0);

        var items = await Store.ListItemsAsync(job.Id);
        items.Count.ShouldBe(3);
        items.Select(static i => i.Seq).ShouldBe([0, 1, 2]);
        items.Select(static i => i.Input).ShouldBe(["a", "b", "c"]);
        items.ShouldAllBe(static i => i.Status == JobItemStatus.Pending);
    }

    [Fact]
    public async Task The_enqueued_job_is_the_job_a_read_reports()
    {
        // The text, the time and the payload the row holds, not those of the call: a SQL store writes U+0000 as
        // U+FFFD, PostgreSQL keeps microseconds and a JSON column does not keep the caller's spacing.
        var queued = TestData.Job() with
        {
            TargetName = "agent\0name",
            Payload = TestData.State("""{ "input" : [ "a", "b" ] }"""),
            ScheduledFor = new DateTimeOffset(2026, 11, 4, 9, 30, 15, TimeSpan.Zero).AddTicks(7),
            CreatedAt = new DateTimeOffset(2026, 11, 4, 9, 30, 14, TimeSpan.Zero).AddTicks(3),
            MaxAttempts = 3,

            // The column keeps whole milliseconds, so the sub-millisecond part is what the call and the row disagree on.
            MaxDuration = TimeSpan.FromTicks(15_000_007),
        };

        var job = await Store.EnqueueAsync(queued, ["a"]);
        var read = (await Store.GetAsync(job.TenantId, job.Id)).ShouldNotBeNull();

        job.TargetName.ShouldBe(read.TargetName);
        job.ScheduledFor.ShouldBe(read.ScheduledFor);
        job.CreatedAt.ShouldBe(read.CreatedAt);
        job.Payload.GetRawText().ShouldBe(read.Payload.GetRawText());
        job.Lane.ShouldBe(read.Lane);
        job.Status.ShouldBe(read.Status);
        job.TotalItems.ShouldBe(read.TotalItems);
        job.MaxAttempts.ShouldBe(read.MaxAttempts);
        job.MaxDuration.ShouldBe(read.MaxDuration);
    }

    [Fact]
    public async Task An_id_that_is_already_queued_is_refused_and_the_queued_job_is_kept()
    {
        // A caller that writes a job a crash left missing can race another caller writing the
        // same job (same id). The store's refusal of the second write is what keeps the work
        // from running twice: a store that replaced the job would reset a leased job to
        // Pending, and a second worker would lease it.
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var owner = Owner();

        (await LeaseAsync(owner, TimeSpan.FromMinutes(5))).ShouldNotBeNull();

        await Should.ThrowAsync<Exception>(async () => await Store.EnqueueAsync(job with { TargetName = "second-writer" }, ["b"]));

        var kept = (await Store.GetAsync(job.TenantId, job.Id)).ShouldNotBeNull();

        kept.Status.ShouldBe(JobStatus.Leased);
        kept.LeaseOwner.ShouldBe(owner);
        kept.TargetName.ShouldBe(job.TargetName);
        (await Store.ListItemsAsync(job.Id)).ShouldHaveSingleItem().Input.ShouldBe("a");
    }

    [Fact]
    public async Task Job_not_yet_due_cannot_be_leased()
    {
        await Store.EnqueueAsync(TestData.Job(scheduledFor: DateTimeOffset.UtcNow.AddMinutes(5)), ["a"]);

        (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5))).ShouldBeNull();
    }

    [Fact]
    public async Task Leasing_marks_Leased_and_increments_the_attempt()
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var owner = Owner();

        var leased = await LeaseAsync(owner, TimeSpan.FromMinutes(5));

        leased.ShouldNotBeNull();
        leased.Id.ShouldBe(job.Id);
        leased.Status.ShouldBe(JobStatus.Leased);
        leased.LeaseOwner.ShouldBe(owner);
        leased.Attempt.ShouldBe(1);
    }

    [Fact]
    public async Task A_jobs_own_time_limit_survives_the_round_trip_and_the_lease()
    {
        // The worker reads the limit from the LEASED record,
        // so the lease query has to return it as well as the read: a store that drops it
        // there runs every job without its limit, silently.
        var job = await Store.EnqueueAsync(TestData.Job() with { MaxDuration = TimeSpan.FromMinutes(90) }, ["a"]);

        job.MaxDuration.ShouldBe(TimeSpan.FromMinutes(90));
        (await Store.GetAsync("default", job.Id))!.MaxDuration.ShouldBe(TimeSpan.FromMinutes(90));
        (await Store.QueryAsync(new JobQuery { TenantId = "default" })).Single().MaxDuration.ShouldBe(TimeSpan.FromMinutes(90));

        var leased = await LeaseAsync(Owner(), TimeSpan.FromMinutes(5));

        leased.ShouldNotBeNull();
        leased.MaxDuration.ShouldBe(TimeSpan.FromMinutes(90));
    }

    [Fact]
    public async Task A_jobs_time_limit_beyond_an_int_of_milliseconds_survives_the_round_trip_and_the_lease()
    {
        // Added. A store that keeps the limit in a 32-bit millisecond column
        // overflows at about 24.8 days and either throws or stores a wrong limit; the
        // 90 minutes above cannot tell it from a correct store. The dispatcher accepts a
        // limit up to the timer ceiling (about 49.7 days), so 30 days is a legal value.
        var limit = TimeSpan.FromDays(30);
        var job = await Store.EnqueueAsync(TestData.Job() with { MaxDuration = limit }, ["a"]);

        job.MaxDuration.ShouldBe(limit);
        (await Store.GetAsync("default", job.Id))!.MaxDuration.ShouldBe(limit);

        var leased = await LeaseAsync(Owner(), TimeSpan.FromMinutes(5));

        leased.ShouldNotBeNull();
        leased.MaxDuration.ShouldBe(limit);
    }

    [Fact]
    public async Task A_job_without_a_time_limit_reads_back_none()
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);

        (await Store.GetAsync("default", job.Id))!.MaxDuration.ShouldBeNull();
        (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5)))!.MaxDuration.ShouldBeNull();
    }

    [Fact]
    public async Task Leased_job_is_not_given_to_another_worker_before_expiry()
    {
        await Store.EnqueueAsync(TestData.Job(), ["a"]);
        await LeaseAsync(Owner(), TimeSpan.FromMinutes(5));

        (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5))).ShouldBeNull();
    }

    [Fact]
    public async Task Job_can_be_re_leased_once_the_lease_expires()
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);

        await LeaseAsync(Owner(), TimeSpan.FromMilliseconds(20));
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        var reclaimed = await LeaseAsync(Owner(), TimeSpan.FromMinutes(5));

        reclaimed.ShouldNotBeNull();
        reclaimed.Id.ShouldBe(job.Id);
        reclaimed.Attempt.ShouldBe(2);
    }

    [Fact]
    public async Task MarkRunningAsync_works_only_for_the_real_owner()
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var lease = await LeaseOfAsync(Owner(), TimeSpan.FromMinutes(5));

        (await Store.MarkRunningAsync(lease with { Owner = Owner() })).ShouldBeFalse();
        (await Store.MarkRunningAsync(lease)).ShouldBeTrue();

        var current = await Store.GetAsync(job.TenantId, job.Id);
        current!.Status.ShouldBe(JobStatus.Running);
    }

    [Fact]
    public async Task ReportItemAsync_is_idempotent()
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a", "b"]);
        var result = new JobItemResult { JobId = job.Id, Seq = 0, Status = JobItemStatus.Completed, RunId = Guid.NewGuid() };

        await Store.ReportItemAsync(result);
        await Store.ReportItemAsync(result);

        var current = await Store.GetAsync(job.TenantId, job.Id);
        current!.DoneItems.ShouldBe(1);
        current.FailedItems.ShouldBe(0);
    }

    /// <summary>
    /// A pending report marks the item with the run that is processing it and
    /// counts nothing; the report that finishes the item is counted once.
    /// </summary>
    /// <remarks>
    /// Workflow recovery writes the run id on the item BEFORE the run starts, so a
    /// job leased again after a crash finds the run the item was processing. A
    /// store that counted the marker, or finished the item on it, would close the
    /// job as done while its work was still in flight.
    /// </remarks>
    [Fact]
    public async Task A_pending_report_marks_the_item_and_counts_nothing()
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var markedRun = Guid.NewGuid();
        var finishingRun = Guid.NewGuid();

        await Store.ReportItemAsync(new JobItemResult { JobId = job.Id, Seq = 0, Status = JobItemStatus.Pending, RunId = markedRun });

        var marked = (await Store.ListItemsAsync(job.Id)).ShouldHaveSingleItem();
        marked.Status.ShouldBe(JobItemStatus.Pending);
        marked.RunId.ShouldBe(markedRun);

        var unchanged = await Store.GetAsync(job.TenantId, job.Id);
        unchanged!.DoneItems.ShouldBe(0);
        unchanged.FailedItems.ShouldBe(0);

        await Store.ReportItemAsync(new JobItemResult { JobId = job.Id, Seq = 0, Status = JobItemStatus.Completed, RunId = finishingRun });

        var finished = (await Store.ListItemsAsync(job.Id)).ShouldHaveSingleItem();
        finished.Status.ShouldBe(JobItemStatus.Completed);
        finished.RunId.ShouldBe(finishingRun);

        var counted = await Store.GetAsync(job.TenantId, job.Id);
        counted!.DoneItems.ShouldBe(1);
        counted.FailedItems.ShouldBe(0);
    }

    /// <summary>
    /// A report for an item that already finished changes neither the item nor
    /// the counters.
    /// </summary>
    /// <remarks>
    /// The SQL stores update an item only while it is pending. A store that
    /// overwrote a finished item let a late report from a worker whose lease had
    /// expired rewrite the result the job already gave, and a marker written
    /// after the item finished would turn it back into a pending item.
    /// </remarks>
    [Fact]
    public async Task A_report_for_a_finished_item_changes_nothing()
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var finishingRun = Guid.NewGuid();

        await Store.ReportItemAsync(new JobItemResult { JobId = job.Id, Seq = 0, Status = JobItemStatus.Completed, RunId = finishingRun });
        await Store.ReportItemAsync(new JobItemResult { JobId = job.Id, Seq = 0, Status = JobItemStatus.Failed, RunId = Guid.NewGuid(), Error = "late" });
        await Store.ReportItemAsync(new JobItemResult { JobId = job.Id, Seq = 0, Status = JobItemStatus.Pending, RunId = Guid.NewGuid() });

        var item = (await Store.ListItemsAsync(job.Id)).ShouldHaveSingleItem();
        item.Status.ShouldBe(JobItemStatus.Completed);
        item.RunId.ShouldBe(finishingRun);
        item.Error.ShouldBeNull();

        var current = await Store.GetAsync(job.TenantId, job.Id);
        current!.DoneItems.ShouldBe(1);
        current.FailedItems.ShouldBe(0);
    }

    [Fact]
    public async Task ReportItemAsync_counts_successful_and_failed_items_separately()
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a", "b"]);

        await Store.ReportItemAsync(new JobItemResult { JobId = job.Id, Seq = 0, Status = JobItemStatus.Completed });
        await Store.ReportItemAsync(new JobItemResult { JobId = job.Id, Seq = 1, Status = JobItemStatus.Failed, Error = "blew up" });

        var current = await Store.GetAsync(job.TenantId, job.Id);
        current!.DoneItems.ShouldBe(1);
        current.FailedItems.ShouldBe(1);

        var items = await Store.ListItemsAsync(job.Id);
        items.Single(static i => i.Seq == 1).Error.ShouldBe("blew up");
        items.Single(static i => i.Seq == 1).Status.ShouldBe(JobItemStatus.Failed);
    }

    [Fact]
    public async Task CompleteAsync_releases_the_lease()
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var lease = await LeaseOfAsync(Owner(), TimeSpan.FromMinutes(5));

        (await Store.CompleteAsync(
            lease,
            new JobCompletion
            {
                Status = JobStatus.Completed,
                CompletedAt = DateTimeOffset.UtcNow,
            })).ShouldBeTrue();

        var current = await Store.GetAsync(job.TenantId, job.Id);
        current!.Status.ShouldBe(JobStatus.Completed);
        current.LeaseOwner.ShouldBeNull();
        current.LeaseUntil.ShouldBeNull();
        current.CompletedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task ReleaseForRetryAsync_returns_to_pending_without_resetting_the_attempt()
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var lease = await LeaseOfAsync(Owner(), TimeSpan.FromMinutes(5));

        (await Store.ReleaseForRetryAsync(lease, "temporary error")).ShouldBeTrue();

        var current = await Store.GetAsync(job.TenantId, job.Id);
        current!.Status.ShouldBe(JobStatus.Pending);
        current.Attempt.ShouldBe(1);
        current.ErrorMessage.ShouldBe("temporary error");

        (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5))).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_cancelled_job_is_changed_by_neither_completion_nor_release()
    {
        // A handler that throws after its job was cancelled reaches the
        // worker's failure path. Before the store refused these writes, the
        // cancelled job became Failed (last attempt) or Pending — and ran again.
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var lease = await LeaseOfAsync(Owner(), TimeSpan.FromMinutes(5));

        (await Store.CancelAsync(job.TenantId, job.Id)).ShouldBeTrue();

        (await Store.CompleteAsync(
            lease,
            new JobCompletion
            {
                Status = JobStatus.Failed,
                CompletedAt = DateTimeOffset.UtcNow,
                ErrorMessage = "thrown after the cancel",
            })).ShouldBeFalse();

        (await Store.ReleaseForRetryAsync(lease, "thrown after the cancel")).ShouldBeFalse();
        (await Store.MarkRunningAsync(lease)).ShouldBeFalse();

        var current = await Store.GetAsync(job.TenantId, job.Id);
        current!.Status.ShouldBe(JobStatus.Cancelled);
        current.ErrorMessage.ShouldBeNull();
        (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5))).ShouldBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_cancelled_job_shows_no_lease_owner_or_end(bool running)
    {
        // Cancelling gives the lease up: a record that kept the worker and the end of the lease would show a job that is
        // closed as one that somebody still holds, and the tenant cap and the queue views read those columns.
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var owner = Owner();
        var lease = await LeaseOfAsync(owner, TimeSpan.FromMinutes(5));

        if (running)
        {
            (await Store.MarkRunningAsync(lease)).ShouldBeTrue();
        }

        var held = (await Store.GetAsync(job.TenantId, job.Id)).ShouldNotBeNull();

        held.LeaseOwner.ShouldBe(owner, "the lease must be visible before the cancel, or the test proves nothing");
        held.LeaseUntil.ShouldNotBeNull();

        (await Store.CancelAsync(job.TenantId, job.Id)).ShouldBeTrue();

        var cancelled = (await Store.GetAsync(job.TenantId, job.Id)).ShouldNotBeNull();

        cancelled.Status.ShouldBe(JobStatus.Cancelled);
        cancelled.LeaseOwner.ShouldBeNull();
        cancelled.LeaseUntil.ShouldBeNull();
    }

    [Fact]
    public async Task A_finished_job_is_not_finalized_a_second_time()
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var lease = await LeaseOfAsync(Owner(), TimeSpan.FromMinutes(5));

        (await Store.CompleteAsync(lease, new JobCompletion { Status = JobStatus.Completed, CompletedAt = DateTimeOffset.UtcNow })).ShouldBeTrue();
        (await Store.CompleteAsync(
            lease,
            new JobCompletion
            {
                Status = JobStatus.Failed,
                CompletedAt = DateTimeOffset.UtcNow,
                ErrorMessage = "late",
            })).ShouldBeFalse();

        var current = await Store.GetAsync(job.TenantId, job.Id);
        current!.Status.ShouldBe(JobStatus.Completed);
        current.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task A_worker_that_lost_its_lease_cannot_close_the_new_owners_attempt()
    {
        // A worker that stalls past its lease (a long pause, a network cut) comes back
        // and writes its outcome. Another worker has leased the job since; the stalled
        // worker's write must not close the attempt that is running.
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var stalled = await LeaseOfAsync(Owner(), TimeSpan.FromMilliseconds(20));

        await Task.Delay(TimeSpan.FromMilliseconds(200)); // delay: product

        var holder = await LeaseOfAsync(Owner(), TimeSpan.FromMinutes(5));
        (await Store.MarkRunningAsync(holder)).ShouldBeTrue();

        (await Store.CompleteAsync(stalled, new JobCompletion { Status = JobStatus.Completed, CompletedAt = DateTimeOffset.UtcNow })).ShouldBeFalse();

        var current = (await Store.GetAsync(job.TenantId, job.Id)).ShouldNotBeNull();

        current.Status.ShouldBe(JobStatus.Running);
        current.LeaseOwner.ShouldBe(holder.Owner);
        current.Attempt.ShouldBe(holder.Attempt);
        current.CompletedAt.ShouldBeNull();

        // The holder is not locked out by the refused write.
        (await Store.CompleteAsync(holder, new JobCompletion { Status = JobStatus.Completed, CompletedAt = DateTimeOffset.UtcNow })).ShouldBeTrue();
        (await Store.GetAsync(job.TenantId, job.Id))!.Status.ShouldBe(JobStatus.Completed);
    }

    [Fact]
    public async Task A_stale_attempt_of_the_same_owner_cannot_complete_the_new_attempt()
    {
        // The worker identity is the same process-wide, so a worker that leases a job
        // again is the SAME owner: only the attempt number tells its two attempts apart.
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var owner = Owner();
        var stale = await LeaseOfAsync(owner, TimeSpan.FromMilliseconds(20));

        await Task.Delay(TimeSpan.FromMilliseconds(200)); // delay: product

        var current = await LeaseOfAsync(owner, TimeSpan.FromMinutes(5));

        current.Owner.ShouldBe(stale.Owner);
        current.Attempt.ShouldBe(stale.Attempt + 1);

        (await Store.MarkRunningAsync(stale)).ShouldBeFalse();
        (await Store.RenewLeaseAsync(stale, TimeSpan.FromMinutes(5))).ShouldBeFalse();
        (await Store.CompleteAsync(stale, new JobCompletion { Status = JobStatus.Completed, CompletedAt = DateTimeOffset.UtcNow })).ShouldBeFalse();
        (await Store.ReleaseForRetryAsync(stale, "stale")).ShouldBeFalse();

        var record = (await Store.GetAsync(job.TenantId, job.Id)).ShouldNotBeNull();

        record.Status.ShouldBe(JobStatus.Leased);
        record.Attempt.ShouldBe(current.Attempt);
        record.ErrorMessage.ShouldBeNull();

        (await Store.MarkRunningAsync(current)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_worker_that_lost_its_lease_cannot_return_the_job_to_the_queue()
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var stalled = await LeaseOfAsync(Owner(), TimeSpan.FromMilliseconds(20));

        await Task.Delay(TimeSpan.FromMilliseconds(200)); // delay: product

        var holder = await LeaseOfAsync(Owner(), TimeSpan.FromMinutes(5));
        (await Store.MarkRunningAsync(holder)).ShouldBeTrue();

        (await Store.ReleaseForRetryAsync(stalled, "stalled worker's failure")).ShouldBeFalse();

        var current = (await Store.GetAsync(job.TenantId, job.Id)).ShouldNotBeNull();

        current.Status.ShouldBe(JobStatus.Running);
        current.LeaseOwner.ShouldBe(holder.Owner);
        current.ErrorMessage.ShouldBeNull();

        // Not back in the queue: no third worker can take it.
        (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5))).ShouldBeNull();
    }

    [Fact]
    public async Task A_released_attempt_cannot_touch_the_attempt_that_replaced_it()
    {
        // A job released for another attempt is leasable at once, and the worker that
        // released it can lease it again before the first attempt has finished returning.
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var owner = Owner();
        var first = await LeaseOfAsync(owner, TimeSpan.FromMinutes(5));

        (await Store.ReleaseForRetryAsync(first, "try again")).ShouldBeTrue();

        var second = await LeaseOfAsync(owner, TimeSpan.FromMinutes(5));

        second.Attempt.ShouldBe(first.Attempt + 1);

        (await Store.CompleteAsync(first, new JobCompletion { Status = JobStatus.Completed, CompletedAt = DateTimeOffset.UtcNow })).ShouldBeFalse();
        (await Store.ReleaseForRetryAsync(first, "again")).ShouldBeFalse();
        (await Store.RenewLeaseAsync(first, TimeSpan.FromMinutes(5))).ShouldBeFalse();

        var record = (await Store.GetAsync(job.TenantId, job.Id)).ShouldNotBeNull();

        record.Status.ShouldBe(JobStatus.Leased);
        record.Attempt.ShouldBe(second.Attempt);
        (await Store.MarkRunningAsync(second)).ShouldBeTrue();
    }

    [Fact]
    public async Task Renewing_a_lease_extends_it_only_for_the_attempt_that_holds_it()
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var lease = await LeaseOfAsync(Owner(), TimeSpan.FromSeconds(30));
        var before = (await Store.GetAsync(job.TenantId, job.Id))!.LeaseUntil.ShouldNotBeNull();

        (await Store.RenewLeaseAsync(lease, TimeSpan.FromMinutes(10))).ShouldBeTrue();

        (await Store.GetAsync(job.TenantId, job.Id))!.LeaseUntil.ShouldNotBeNull().ShouldBeGreaterThan(before);

        // The worker renews for as long as its handler runs, so a Running job's lease is renewed too.
        (await Store.MarkRunningAsync(lease)).ShouldBeTrue();
        (await Store.RenewLeaseAsync(lease, TimeSpan.FromMinutes(10))).ShouldBeTrue();

        (await Store.RenewLeaseAsync(lease with { Owner = Owner() }, TimeSpan.FromMinutes(10))).ShouldBeFalse();
        (await Store.RenewLeaseAsync(lease with { Attempt = lease.Attempt + 1 }, TimeSpan.FromMinutes(10))).ShouldBeFalse();
        (await Store.RenewLeaseAsync(lease with { JobId = Guid.NewGuid() }, TimeSpan.FromMinutes(10))).ShouldBeFalse();
    }

    [Fact]
    public async Task Renewing_the_lease_of_a_cancelled_or_finished_job_returns_false()
    {
        // Cancelling clears the lease, so a worker that renews it learns the job is
        // no longer its own.
        var cancelled = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var cancelledLease = await LeaseOfAsync(Owner(), TimeSpan.FromMinutes(5));

        (await Store.CancelAsync(cancelled.TenantId, cancelled.Id)).ShouldBeTrue();
        (await Store.RenewLeaseAsync(cancelledLease, TimeSpan.FromMinutes(5))).ShouldBeFalse();

        var finished = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var finishedLease = await LeaseOfAsync(Owner(), TimeSpan.FromMinutes(5));

        finishedLease.JobId.ShouldBe(finished.Id);
        (await Store.CompleteAsync(finishedLease, new JobCompletion { Status = JobStatus.Completed, CompletedAt = DateTimeOffset.UtcNow })).ShouldBeTrue();
        (await Store.RenewLeaseAsync(finishedLease, TimeSpan.FromMinutes(5))).ShouldBeFalse();
    }

    [Fact]
    public async Task Completing_or_releasing_with_another_workers_lease_changes_nothing()
    {
        // Same job, same attempt, different owner: the attempt number alone does not name a lease.
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var lease = await LeaseOfAsync(Owner(), TimeSpan.FromMinutes(5));
        var other = lease with { Owner = Owner() };

        (await Store.MarkRunningAsync(lease)).ShouldBeTrue();

        (await Store.CompleteAsync(other, new JobCompletion { Status = JobStatus.Completed, CompletedAt = DateTimeOffset.UtcNow })).ShouldBeFalse();
        (await Store.ReleaseForRetryAsync(other, "not mine")).ShouldBeFalse();

        var current = (await Store.GetAsync(job.TenantId, job.Id)).ShouldNotBeNull();

        current.Status.ShouldBe(JobStatus.Running);
        current.LeaseOwner.ShouldBe(lease.Owner);
        current.ErrorMessage.ShouldBeNull();
        current.CompletedAt.ShouldBeNull();
    }

    [Fact]
    public async Task A_worker_whose_name_differs_only_in_letter_case_does_not_hold_the_lease()
    {
        // The caller picks the worker name (JobLeaseRequest.Owner), so "Worker-A" and "worker-a" are two workers. A store
        // that compares the owner with the rules of a case-insensitive column lets the second one renew, start, complete
        // and release the first one's lease.
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var lease = await LeaseOfAsync($"Worker-{Guid.NewGuid():N}", TimeSpan.FromMinutes(5));
        var lookalike = lease with { Owner = lease.Owner.ToLowerInvariant() };

        string.Equals(lookalike.Owner, lease.Owner, StringComparison.Ordinal).ShouldBeFalse();
        (await Store.RenewLeaseAsync(lookalike, TimeSpan.FromMinutes(5))).ShouldBeFalse();
        (await Store.MarkRunningAsync(lookalike)).ShouldBeFalse();
        (await Store.CompleteAsync(lookalike, new JobCompletion { Status = JobStatus.Completed, CompletedAt = DateTimeOffset.UtcNow })).ShouldBeFalse();
        (await Store.ReleaseForRetryAsync(lookalike, "not mine")).ShouldBeFalse();

        var current = (await Store.GetAsync(job.TenantId, job.Id)).ShouldNotBeNull();

        current.Status.ShouldBe(JobStatus.Leased);
        current.LeaseOwner.ShouldBe(lease.Owner);
        current.ErrorMessage.ShouldBeNull();

        // The worker that holds the lease is not locked out by the refused writes.
        (await Store.MarkRunningAsync(lease)).ShouldBeTrue();
        (await Store.CompleteAsync(lease, new JobCompletion { Status = JobStatus.Completed, CompletedAt = DateTimeOffset.UtcNow })).ShouldBeTrue();
    }

    [Fact]
    public async Task Only_one_of_two_concurrent_completions_of_the_same_lease_is_applied()
    {
        // The window two overlapping completions need is a few instructions wide in a store that checks the lease and
        // then writes: the two callers start on threads of their own, released together, and the pair runs against a
        // fresh job many times, so a store that is not atomic loses a round and not only once in a while.
        for (var round = 0; round < ConcurrentRounds; round++)
        {
            var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);
            var lease = await LeaseOfAsync(Owner(), TimeSpan.FromMinutes(5));
            (await Store.MarkRunningAsync(lease)).ShouldBeTrue();

            var completed = new JobCompletion { Status = JobStatus.Completed, CompletedAt = DateTimeOffset.UtcNow };
            var failed = new JobCompletion { Status = JobStatus.Failed, CompletedAt = DateTimeOffset.UtcNow, ErrorMessage = "second writer" };

            var results = SimultaneousCalls.Run(
                2,
                index => Store.CompleteAsync(lease, index == 0 ? completed : failed).AsTask().GetAwaiter().GetResult());

            results.Count(static applied => applied).ShouldBe(1, $"round {round}: both completions were applied, or neither");

            var current = (await Store.GetAsync(job.TenantId, job.Id)).ShouldNotBeNull();

            // The record is the winner's, whole: not one writer's status with the other's message.
            if (current.Status == JobStatus.Completed)
            {
                current.ErrorMessage.ShouldBeNull($"round {round}");
            }
            else
            {
                current.Status.ShouldBe(JobStatus.Failed, $"round {round}");
                current.ErrorMessage.ShouldBe("second writer", $"round {round}");
            }
        }
    }

    [Theory]
    [InlineData(JobStatus.Pending, true)]
    [InlineData(JobStatus.Completed, false)]
    [InlineData(JobStatus.Failed, false)]
    public async Task CancelAsync_works_only_in_eligible_states(JobStatus status, bool expected)
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);

        if (status is JobStatus.Completed or JobStatus.Failed)
        {
            var lease = await LeaseOfAsync(Owner(), TimeSpan.FromMinutes(5));

            (await Store.CompleteAsync(lease, new JobCompletion { Status = status, CompletedAt = DateTimeOffset.UtcNow })).ShouldBeTrue();
        }

        (await Store.CancelAsync(job.TenantId, job.Id)).ShouldBe(expected);
    }

    [Fact]
    public async Task CancelAsync_does_not_affect_another_tenants_job()
    {
        var job = await Store.EnqueueAsync(TestData.Job(tenantId: "tenant-a"), ["a"]);

        (await Store.CancelAsync("tenant-b", job.Id)).ShouldBeFalse();

        var current = await Store.GetAsync("tenant-a", job.Id);
        current!.Status.ShouldBe(JobStatus.Pending);
    }

    [Fact]
    public async Task GetAsync_returns_null_for_another_tenant()
    {
        var job = await Store.EnqueueAsync(TestData.Job(tenantId: "tenant-a"), ["a"]);

        (await Store.GetAsync("tenant-b", job.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task QueryAsync_filters_by_tenant()
    {
        await Store.EnqueueAsync(TestData.Job(tenantId: "tenant-a"), ["a"]);
        await Store.EnqueueAsync(TestData.Job(tenantId: "tenant-b"), ["a"]);

        var results = await Store.QueryAsync(new JobQuery { TenantId = "tenant-a" });

        results.ShouldHaveSingleItem();
        results[0].TenantId.ShouldBe("tenant-a");
    }

    [Fact]
    public async Task QueryAsync_filters_by_handler_key()
    {
        await Store.EnqueueAsync(
            TestData.Job() with { HandlerKey = JobHandlerKeys.AgentBatch }, ["a"]);
        await Store.EnqueueAsync(
            TestData.Job() with { HandlerKey = JobHandlerKeys.Retention }, ["a"]);

        var results = await Store.QueryAsync(
            new JobQuery { HandlerKey = JobHandlerKeys.Retention });

        results.ShouldHaveSingleItem();
        results[0].HandlerKey.ShouldBe(JobHandlerKeys.Retention);
    }

    [Fact]
    public async Task QueryAsync_returns_nothing_for_a_handler_key_no_job_uses()
    {
        await Store.EnqueueAsync(TestData.Job() with { HandlerKey = JobHandlerKeys.AgentBatch }, ["a"]);

        (await Store.QueryAsync(new JobQuery { HandlerKey = "contoso.absent" })).ShouldBeEmpty();
    }

    [Fact]
    public async Task QueryAsync_treats_an_empty_handler_key_as_a_filter_that_matches_nothing()
    {
        // 🚨 An EXPLICIT empty string is a filter, not "no filter" — the two
        // implementations diverged here before. `null` means unfiltered; the
        // empty string is a value no valid key can equal, so it matches nothing.
        await Store.EnqueueAsync(TestData.Job() with { HandlerKey = JobHandlerKeys.AgentBatch }, ["a"]);

        (await Store.QueryAsync(new JobQuery { HandlerKey = "" })).ShouldBeEmpty();
        (await Store.QueryAsync(new JobQuery { HandlerKey = null })).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Enqueued_job_defaults_to_the_default_lane()
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);

        job.Lane.ShouldBe(JobLanes.Default);
    }

    [Fact]
    public async Task Lease_with_a_lane_filter_only_returns_a_job_in_that_lane()
    {
        await Store.EnqueueAsync(TestData.Job() with { Lane = "default" }, ["a"]);
        var mediaJob = await Store.EnqueueAsync(TestData.Job() with { Lane = "media" }, ["a"]);

        var leased = await LeaseAsync(Owner(), TimeSpan.FromMinutes(5), ["media"]);

        leased.ShouldNotBeNull();
        leased.Id.ShouldBe(mediaJob.Id);
        leased.Lane.ShouldBe("media");
    }

    [Fact]
    public async Task Lease_with_a_lane_filter_never_returns_a_job_from_an_unlisted_lane()
    {
        await Store.EnqueueAsync(TestData.Job() with { Lane = "default" }, ["a"]);

        (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5), ["media"])).ShouldBeNull();
    }

    [Fact]
    public async Task Lease_with_null_lanes_applies_no_filter()
    {
        await Store.EnqueueAsync(TestData.Job() with { Lane = "media" }, ["a"]);

        (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5), lanes: null)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Lease_with_an_empty_lanes_list_applies_no_filter()
    {
        await Store.EnqueueAsync(TestData.Job() with { Lane = "media" }, ["a"]);

        (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5), lanes: [])).ShouldNotBeNull();
    }

    [Fact]
    public async Task Lease_with_a_tenant_cap_skips_a_tenant_at_the_cap_and_leases_the_oldest_job_of_another()
    {
        // Tenant A holds the three oldest jobs, tenant B has one newer job: the plain order would give A
        // everything first.
        var oldest = await Store.EnqueueAsync(WorkflowJob("tenant-a", age: 30), ["a"]);
        await Store.EnqueueAsync(WorkflowJob("tenant-a", age: 20), ["a"]);
        await Store.EnqueueAsync(WorkflowJob("tenant-a", age: 10), ["a"]);
        var other = await Store.EnqueueAsync(WorkflowJob("tenant-b", age: 5), ["a"]);

        var first = await LeaseAsync(Owner(), TimeSpan.FromMinutes(5), tenantCap: WorkflowCap(1));
        var second = await LeaseAsync(Owner(), TimeSpan.FromMinutes(5), tenantCap: WorkflowCap(1));
        var third = await LeaseAsync(Owner(), TimeSpan.FromMinutes(5), tenantCap: WorkflowCap(1));

        first.ShouldNotBeNull().Id.ShouldBe(oldest.Id);
        second.ShouldNotBeNull().Id.ShouldBe(other.Id);
        third.ShouldBeNull();
    }

    [Fact]
    public async Task Lease_without_a_tenant_cap_gives_one_tenant_the_whole_queue_in_age_order()
    {
        // The same queue as above, leased the way it always was: the cap is what changes the order.
        var oldest = await Store.EnqueueAsync(WorkflowJob("tenant-a", age: 30), ["a"]);
        var second = await Store.EnqueueAsync(WorkflowJob("tenant-a", age: 20), ["a"]);
        await Store.EnqueueAsync(WorkflowJob("tenant-b", age: 5), ["a"]);

        (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5)))!.Id.ShouldBe(oldest.Id);
        (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5)))!.Id.ShouldBe(second.Id);
    }

    [Fact]
    public async Task Lease_with_a_tenant_cap_leases_up_to_the_cap_for_one_tenant()
    {
        await Store.EnqueueAsync(WorkflowJob("tenant-a", age: 30), ["a"]);
        await Store.EnqueueAsync(WorkflowJob("tenant-a", age: 20), ["a"]);
        await Store.EnqueueAsync(WorkflowJob("tenant-a", age: 10), ["a"]);

        (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5), tenantCap: WorkflowCap(2))).ShouldNotBeNull();
        (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5), tenantCap: WorkflowCap(2))).ShouldNotBeNull();
        (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5), tenantCap: WorkflowCap(2))).ShouldBeNull();
    }

    [Fact]
    public async Task Lease_with_a_tenant_cap_counts_both_workflow_handler_keys_together()
    {
        await Store.EnqueueAsync(WorkflowJob("tenant-a", age: 30, JobHandlerKeys.Workflow), ["a"]);
        await Store.EnqueueAsync(WorkflowJob("tenant-a", age: 20, JobHandlerKeys.WorkflowRun), ["a"]);

        (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5), tenantCap: WorkflowCap(1))).ShouldNotBeNull();
        (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5), tenantCap: WorkflowCap(1))).ShouldBeNull();
    }

    [Fact]
    public async Task Lease_with_a_tenant_cap_still_leases_a_job_of_another_handler_key_of_a_tenant_at_the_cap()
    {
        await Store.EnqueueAsync(WorkflowJob("tenant-a", age: 30), ["a"]);
        await Store.EnqueueAsync(WorkflowJob("tenant-a", age: 20), ["a"]);
        var delivery = await Store.EnqueueAsync(WorkflowJob("tenant-a", age: 10, JobHandlerKeys.WebhookDelivery), ["a"]);

        (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5), tenantCap: WorkflowCap(1))).ShouldNotBeNull();

        // The tenant is at the cap for workflows; the older workflow job is skipped, the delivery is not.
        var leased = await LeaseAsync(Owner(), TimeSpan.FromMinutes(5), tenantCap: WorkflowCap(1));

        leased.ShouldNotBeNull().Id.ShouldBe(delivery.Id);
    }

    [Fact]
    public async Task Lease_with_a_tenant_cap_does_not_count_a_job_of_another_handler_key()
    {
        // A tenant's leased delivery is not a workflow: it does not use up the workflow cap.
        await Store.EnqueueAsync(WorkflowJob("tenant-a", age: 30, JobHandlerKeys.WebhookDelivery), ["a"]);
        await Store.EnqueueAsync(WorkflowJob("tenant-a", age: 20), ["a"]);

        (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5))).ShouldNotBeNull();

        (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5), tenantCap: WorkflowCap(1))).ShouldNotBeNull();
    }

    [Fact]
    public async Task Lease_with_a_tenant_cap_counts_a_running_job()
    {
        await Store.EnqueueAsync(WorkflowJob("tenant-a", age: 30), ["a"]);
        await Store.EnqueueAsync(WorkflowJob("tenant-a", age: 20), ["a"]);

        var first = JobLease.Of((await LeaseAsync(Owner(), TimeSpan.FromMinutes(5), tenantCap: WorkflowCap(1))).ShouldNotBeNull());
        (await Store.MarkRunningAsync(first)).ShouldBeTrue();

        (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5), tenantCap: WorkflowCap(1))).ShouldBeNull();
    }

    [Fact]
    public async Task Lease_with_a_tenant_cap_does_not_count_a_lease_that_has_expired()
    {
        await Store.EnqueueAsync(WorkflowJob("tenant-a", age: 30), ["a"]);
        await Store.EnqueueAsync(WorkflowJob("tenant-a", age: 20), ["a"]);

        (await LeaseAsync(Owner(), TimeSpan.FromMilliseconds(20), tenantCap: WorkflowCap(1))).ShouldNotBeNull();
        await Task.Delay(TimeSpan.FromMilliseconds(200)); // delay: product

        // The dead worker's lease no longer holds the tenant at the cap: a job is leased again.
        (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5), tenantCap: WorkflowCap(1))).ShouldNotBeNull();
    }

    [Fact]
    public async Task Lease_with_a_tenant_cap_does_not_count_the_leases_of_another_tenant()
    {
        await Store.EnqueueAsync(WorkflowJob("tenant-a", age: 30), ["a"]);
        var other = await Store.EnqueueAsync(WorkflowJob("tenant-b", age: 20), ["a"]);

        (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5), tenantCap: WorkflowCap(1))).ShouldNotBeNull();

        // Tenant A holds its one lease; tenant B holds none, so its job is leased.
        (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5), tenantCap: WorkflowCap(1)))!.Id.ShouldBe(other.Id);
    }

    [Fact]
    public async Task Lease_with_a_tenant_cap_still_honors_the_lane_filter()
    {
        await Store.EnqueueAsync(WorkflowJob("tenant-a", age: 30) with { Lane = "media" }, ["a"]);
        var inLane = await Store.EnqueueAsync(WorkflowJob("tenant-b", age: 20) with { Lane = "media" }, ["a"]);
        await Store.EnqueueAsync(WorkflowJob("tenant-c", age: 40), ["a"]);

        var leased = await LeaseAsync(Owner(), TimeSpan.FromMinutes(5), ["media"], WorkflowCap(1));
        var next = await LeaseAsync(Owner(), TimeSpan.FromMinutes(5), ["media"], WorkflowCap(1));

        leased.ShouldNotBeNull().Lane.ShouldBe("media");
        next.ShouldNotBeNull().Id.ShouldBe(inLane.Id);
        (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5), ["media"], WorkflowCap(1))).ShouldBeNull();
    }

    [Fact]
    public async Task Lease_with_a_tenant_cap_that_names_no_handler_key_or_no_room_is_refused()
    {
        await Store.EnqueueAsync(WorkflowJob("tenant-a", age: 30), ["a"]);

        await Should.ThrowAsync<ArgumentException>(
            async () => await LeaseAsync(Owner(), TimeSpan.FromMinutes(5), tenantCap: new JobTenantCap { HandlerKeys = [], MaxLeasedPerTenant = 1 }));
        await Should.ThrowAsync<ArgumentException>(
            async () => await LeaseAsync(Owner(), TimeSpan.FromMinutes(5), tenantCap: WorkflowCap(0)));

        // Neither call leased the job.
        (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5))).ShouldNotBeNull();
    }

    [Fact]
    public async Task Retry_preserves_the_jobs_lane()
    {
        var job = await Store.EnqueueAsync(TestData.Job() with { Lane = "media" }, ["a"]);
        var lease = await LeaseOfAsync(Owner(), TimeSpan.FromMinutes(5));

        (await Store.ReleaseForRetryAsync(lease, "temporary error")).ShouldBeTrue();

        var current = await Store.GetAsync(job.TenantId, job.Id);
        current!.Lane.ShouldBe("media");
    }

    [Fact]
    public async Task QueryAsync_lane_filter_does_not_leak_another_tenants_job()
    {
        await Store.EnqueueAsync(TestData.Job(tenantId: "tenant-a") with { Lane = "media" }, ["a"]);
        var ownJob = await Store.EnqueueAsync(TestData.Job(tenantId: "tenant-b") with { Lane = "media" }, ["a"]);

        var results = await Store.QueryAsync(new JobQuery { TenantId = "tenant-b", Lane = "media" });

        results.ShouldHaveSingleItem();
        results[0].Id.ShouldBe(ownJob.Id);
    }

    [Fact]
    public async Task QueryAsync_filters_by_lane()
    {
        await Store.EnqueueAsync(TestData.Job() with { Lane = "default" }, ["a"]);
        var mediaJob = await Store.EnqueueAsync(TestData.Job() with { Lane = "media" }, ["a"]);

        var results = await Store.QueryAsync(new JobQuery { Lane = "media" });

        results.ShouldHaveSingleItem();
        results[0].Id.ShouldBe(mediaJob.Id);
    }

    [Fact]
    public async Task Queue_depth_is_empty_when_no_job_is_open()
        => (await Store.GetQueueDepthAsync()).ShouldBeEmpty();

    [Fact]
    public async Task Queue_depth_groups_pending_jobs_by_lane()
    {
        await Store.EnqueueAsync(TestData.Job() with { Lane = "default" }, ["a"]);
        await Store.EnqueueAsync(TestData.Job() with { Lane = "default" }, ["a"]);
        await Store.EnqueueAsync(TestData.Job() with { Lane = "media" }, ["a"]);

        var depth = await Store.GetQueueDepthAsync();

        Count(depth, "default", JobStatus.Pending).ShouldBe(2);
        Count(depth, "media", JobStatus.Pending).ShouldBe(1);
    }

    [Fact]
    public async Task Queue_depth_separates_the_open_statuses()
    {
        await Store.EnqueueAsync(TestData.Job(), ["a"]);
        await Store.EnqueueAsync(TestData.Job(), ["a"]);
        await Store.EnqueueAsync(TestData.Job(), ["a"]);

        // Lease twice: the first leased job is moved on to Running, so all
        // three open statuses are represented at once.
        var first = await LeaseOfAsync(Owner(), TimeSpan.FromMinutes(5));
        (await Store.MarkRunningAsync(first)).ShouldBeTrue();
        await LeaseAsync(Owner(), TimeSpan.FromMinutes(5));

        var depth = await Store.GetQueueDepthAsync();

        Count(depth, JobLanes.Default, JobStatus.Pending).ShouldBe(1);
        Count(depth, JobLanes.Default, JobStatus.Leased).ShouldBe(1);
        Count(depth, JobLanes.Default, JobStatus.Running).ShouldBe(1);
    }

    [Fact]
    public async Task Queue_depth_never_counts_a_terminal_status()
    {
        var first = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var second = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        await Store.EnqueueAsync(TestData.Job(), ["a"]);

        // Whichever job the lease picks is the one completed; the other of the first two is cancelled.
        var completed = await LeaseOfAsync(Owner(), TimeSpan.FromMinutes(5));

        (await Store.CompleteAsync(
            completed,
            new JobCompletion
            {
                Status = JobStatus.Completed,
                CompletedAt = DateTimeOffset.UtcNow,
            })).ShouldBeTrue();

        var cancelled = completed.JobId == first.Id ? second : first;

        (await Store.CancelAsync(cancelled.TenantId, cancelled.Id)).ShouldBeTrue();

        var depth = await Store.GetQueueDepthAsync();

        depth.ShouldAllBe(static row =>
            row.Status == JobStatus.Pending || row.Status == JobStatus.Leased || row.Status == JobStatus.Running);
        depth.Sum(static row => row.Count).ShouldBe(1);
    }

    [Fact]
    public async Task Queue_depth_counts_every_tenants_jobs_together()
    {
        await Store.EnqueueAsync(TestData.Job(tenantId: "tenant-a"), ["a"]);
        await Store.EnqueueAsync(TestData.Job(tenantId: "tenant-b"), ["a"]);

        var depth = await Store.GetQueueDepthAsync();

        // Queue depth is an operator signal about the worker pool, which leases
        // across every tenant; it is deliberately NOT filtered or tagged by tenant.
        Count(depth, JobLanes.Default, JobStatus.Pending).ShouldBe(2);
    }

    [Fact]
    public async Task Queue_depth_drops_a_lane_once_its_last_job_finishes()
    {
        await Store.EnqueueAsync(TestData.Job() with { Lane = "media" }, ["a"]);
        var lease = await LeaseOfAsync(Owner(), TimeSpan.FromMinutes(5));

        (await Store.CompleteAsync(
            lease,
            new JobCompletion
            {
                Status = JobStatus.Completed,
                CompletedAt = DateTimeOffset.UtcNow,
            })).ShouldBeTrue();

        // A pair with no open jobs is omitted, not reported as zero.
        (await Store.GetQueueDepthAsync()).ShouldNotContain(static row => row.Lane == "media");
    }

    [Fact]
    public async Task Queue_depth_counts_a_job_released_for_retry_as_pending_again()
    {
        await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var lease = await LeaseOfAsync(Owner(), TimeSpan.FromMinutes(5));

        (await Store.ReleaseForRetryAsync(lease, "temporary error")).ShouldBeTrue();

        var depth = await Store.GetQueueDepthAsync();

        Count(depth, JobLanes.Default, JobStatus.Pending).ShouldBe(1);
        Count(depth, JobLanes.Default, JobStatus.Leased).ShouldBe(0);
    }

    private static long Count(IReadOnlyList<JobQueueDepth> depth, string lane, JobStatus status)
        => depth
            .Where(row => string.Equals(row.Lane, lane, StringComparison.Ordinal) && row.Status == status)
            .Sum(static row => row.Count);

    private async Task<JobLease> LeaseOfAsync(string owner, TimeSpan leaseDuration)
        => JobLease.Of((await LeaseAsync(owner, leaseDuration)).ShouldNotBeNull());

    private ValueTask<JobRecord?> LeaseAsync(
        string owner,
        TimeSpan leaseDuration,
        IReadOnlyList<string>? lanes = null,
        JobTenantCap? tenantCap = null)
        => Store.LeaseAsync(new JobLeaseRequest
        {
            Owner = owner,
            LeaseDuration = leaseDuration,
            Lanes = lanes,
            TenantCap = tenantCap,
        });

    private static JobTenantCap WorkflowCap(int max)
        => new() { HandlerKeys = [JobHandlerKeys.Workflow, JobHandlerKeys.WorkflowRun], MaxLeasedPerTenant = max };

    /// <summary>A workflow job that is already due, <paramref name="age"/> old: a larger age is older and is leased first.</summary>
    private static JobRecord WorkflowJob(string tenantId, int age, string handlerKey = JobHandlerKeys.Workflow)
        => TestData.Job(tenantId, DateTimeOffset.UtcNow - TimeSpan.FromMinutes(age)) with { HandlerKey = handlerKey };

    private static string Owner() => $"worker-{Guid.NewGuid():N}";

    /// <summary>
    /// A NUL character in a job's payload or in an item's input does not fail the enqueue.
    /// </summary>
    /// <remarks>
    /// PostgreSQL cannot hold U+0000 in a <c>text</c> value or in the escape <c>\u0000</c> of a
    /// <c>jsonb</c> value; a request body can carry either. A store may keep it or write
    /// U+FFFD in its place; it must not refuse the job.
    /// </remarks>
    [Fact]
    public async Task A_job_whose_payload_and_input_hold_a_NUL_character_is_stored()
    {
        var job = await Store.EnqueueAsync(
            TestData.Job() with { Payload = TestData.State("[\"before\\u0000after\"]") },
            ["before\0after"]);

        var kept = (await Store.GetAsync(job.TenantId, job.Id)).ShouldNotBeNull();
        kept.Payload.EnumerateArray().Single().GetString().ShouldBeOneOf("before\0after", "before\uFFFDafter");

        (await Store.ListItemsAsync(job.Id)).ShouldHaveSingleItem().Input.ShouldBeOneOf("before\0after", "before\uFFFDafter");
    }

    /// <summary>
    /// Two spellings of one tenant use one share of the cap.
    /// </summary>
    /// <remarks>
    /// The cap exists to stop one tenant from taking every worker. A store that counts the raw text lets a
    /// tenant that writes its identifier in two spellings take twice its share.
    /// </remarks>
    [Fact]
    public async Task Lease_with_a_tenant_cap_counts_two_spellings_of_one_tenant_together()
    {
        await Store.EnqueueAsync(WorkflowJob("Tenant-A", age: 30), ["a"]);
        await Store.EnqueueAsync(WorkflowJob("tenant-a", age: 20), ["a"]);

        var leased = (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5), tenantCap: WorkflowCap(1))).ShouldNotBeNull();

        leased.TenantId.ShouldBe("tenant-a");
        (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5), tenantCap: WorkflowCap(1))).ShouldBeNull();
    }

    /// <summary>The durations a lease cannot have: zero or less, under a whole millisecond, or past a little over 68 years.</summary>
    public static TheoryData<TimeSpan> InvalidLeaseDurations => LeaseDurationCases.Invalid();

    /// <summary>The delays a retry cannot wait: below zero, or past a little over 68 years.</summary>
    public static TheoryData<TimeSpan> InvalidRetryDelays => LeaseDurationCases.InvalidRetryDelays();

    /// <summary>
    /// A lease request whose duration is outside one millisecond to a little over 68 years is refused with
    /// <see cref="ArgumentOutOfRangeException"/>, and no attempt is spent.
    /// </summary>
    /// <remarks>
    /// A store that wrote the duration it was given leased the job for a time that was already over, which another worker
    /// took as a second attempt at once, or for a length it cut without saying so. The check is the first thing the call
    /// does, so the job is as it was: the next request with a real duration leases it as attempt one.
    /// </remarks>
    /// <param name="duration">The duration.</param>
    [Theory]
    [MemberData(nameof(InvalidLeaseDurations))]
    public async Task LeaseAsync_refuses_a_duration_outside_the_lease_range_and_spends_no_attempt(TimeSpan duration)
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () => await LeaseAsync(Owner(), duration));

        var leased = (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5))).ShouldNotBeNull();

        leased.Id.ShouldBe(job.Id);
        leased.Attempt.ShouldBe(1);
    }

    /// <summary>
    /// A renewal with a duration outside the lease range is refused with <see cref="ArgumentOutOfRangeException"/> and
    /// leaves the lease as it was.
    /// </summary>
    /// <param name="duration">The duration.</param>
    [Theory]
    [MemberData(nameof(InvalidLeaseDurations))]
    public async Task RenewLeaseAsync_refuses_a_duration_outside_the_lease_range_and_keeps_the_lease(TimeSpan duration)
    {
        await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var lease = await LeaseOfAsync(Owner(), TimeSpan.FromMinutes(5));

        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () => await Store.RenewLeaseAsync(lease, duration));

        // Still leased for the five minutes it was taken for: another worker finds nothing to take, and the holder can
        // still renew and write.
        (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5))).ShouldBeNull();
        (await Store.RenewLeaseAsync(lease, TimeSpan.FromMinutes(5))).ShouldBeTrue();
    }

    /// <summary>
    /// A retry delay below zero or past a little over 68 years is refused with <see cref="ArgumentOutOfRangeException"/>,
    /// and the job stays leased by the worker that asked.
    /// </summary>
    /// <remarks>
    /// A negative delay is not "at once": the documented ways to ask for that are <see langword="null"/> and zero. A store
    /// that took a negative number for one hid the caller's arithmetic mistake.
    /// </remarks>
    /// <param name="delay">The delay.</param>
    [Theory]
    [MemberData(nameof(InvalidRetryDelays))]
    public async Task ReleaseForRetryAsync_refuses_a_delay_outside_the_retry_range_and_keeps_the_job_leased(TimeSpan delay)
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var lease = await LeaseOfAsync(Owner(), TimeSpan.FromMinutes(5));

        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            async () => await Store.ReleaseForRetryAsync(lease, "retry", delay));

        (await Store.GetAsync(job.TenantId, job.Id)).ShouldNotBeNull().Status.ShouldBe(JobStatus.Leased);
        (await Store.ReleaseForRetryAsync(lease, "retry")).ShouldBeTrue();
    }

    /// <summary>
    /// The range is checked before the cancellation token: a cancelled call whose duration cannot be accepted reports the
    /// duration.
    /// </summary>
    [Fact]
    public async Task A_refused_duration_is_reported_before_a_cancelled_token()
    {
        await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var lease = await LeaseOfAsync(Owner(), TimeSpan.FromMinutes(5));

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            async () => await Store.RenewLeaseAsync(lease, TimeSpan.Zero, cancelled.Token));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            async () => await Store.ReleaseForRetryAsync(lease, "retry", TimeSpan.FromMilliseconds(-1), cancelled.Token));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            async () => await Store.LeaseAsync(
                new JobLeaseRequest { Owner = Owner(), LeaseDuration = TimeSpan.Zero },
                cancelled.Token));
    }

    /// <summary>One millisecond and the longest duration are the two ends of the lease range, and both are accepted.</summary>
    [Fact]
    public async Task The_two_ends_of_the_lease_range_are_accepted()
    {
        await Store.EnqueueAsync(TestData.Job(), ["a"]);
        await Store.EnqueueAsync(TestData.Job(), ["a"]);

        // The longest lease goes first: it stays held, so the second request leases the other job and not this one again.
        var longest = JobLease.Of((await LeaseAsync(Owner(), LeaseDurationCases.Longest)).ShouldNotBeNull());
        var shortest = JobLease.Of((await LeaseAsync(Owner(), LeaseDurationCases.Shortest)).ShouldNotBeNull());

        // The longest lease is a lease: it is renewed at the same length and at the shortest one.
        (await Store.RenewLeaseAsync(longest, LeaseDurationCases.Longest)).ShouldBeTrue();
        (await Store.RenewLeaseAsync(longest, LeaseDurationCases.Shortest)).ShouldBeTrue();

        // The shortest lease may be over by now; whether it is renewable is the store's own business, a refusal of the
        // duration is not.
        _ = await Store.RenewLeaseAsync(shortest, LeaseDurationCases.Shortest);
    }

    /// <summary>
    /// A retry delay of <see langword="null"/>, zero, one millisecond or the longest duration is accepted: the job returns
    /// to <see cref="JobStatus.Pending"/>.
    /// </summary>
    /// <param name="milliseconds">The delay in milliseconds; negative stands for no delay (<see langword="null"/>).</param>
    [Theory]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(LeaseDurationCases.LongestMilliseconds)]
    public async Task ReleaseForRetryAsync_accepts_every_delay_in_the_retry_range(long milliseconds)
    {
        TimeSpan? delay = milliseconds < 0 ? null : TimeSpan.FromMilliseconds(milliseconds);
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var lease = await LeaseOfAsync(Owner(), TimeSpan.FromMinutes(5));

        (await Store.ReleaseForRetryAsync(lease, "retry", delay)).ShouldBeTrue();

        (await Store.GetAsync(job.TenantId, job.Id)).ShouldNotBeNull().Status.ShouldBe(JobStatus.Pending);
    }

    /// <summary>
    /// The job a call returns carries the canonical tenant, as the record a later read returns does.
    /// </summary>
    /// <remarks>
    /// The endpoints compare a job's tenant with the request's as ordinal text. A store that returns the spelling it
    /// was given makes the answer of the enqueue differ from the answer of the next read.
    /// </remarks>
    [Fact]
    public async Task A_job_queued_under_another_spelling_is_returned_and_read_under_the_canonical_tenant()
    {
        var queued = await Store.EnqueueAsync(TestData.Job(tenantId: "TENANT-A"), ["a"]);

        queued.TenantId.ShouldBe("tenant-a");
        (await Store.GetAsync("Tenant-A", queued.Id)).ShouldNotBeNull().TenantId.ShouldBe("tenant-a");
        (await Store.QueryAsync(new JobQuery { TenantId = "tenant-A" })).ShouldHaveSingleItem().TenantId.ShouldBe("tenant-a");
    }
}
