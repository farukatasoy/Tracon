
namespace Tracon.Testing.Contracts.Storage;

/// <summary>
/// Behavior tests for the <see cref="IJobStore"/> contract.
/// </summary>
/// <remarks>
/// The in-memory store and the PostgreSQL store must pass the same
/// scenarios. The lease-expiry test runs against real time (a short lease +
/// a short wait): the PostgreSQL implementation does not take its clock
/// through an injectable <see cref="TimeProvider"/>, so fake time cannot be
/// used here.
/// </remarks>
public abstract class JobStoreContract : TenantIsolationContract<IJobStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
        => (await Store.EnqueueAsync(TestData.Job(tenantId), [name])).Id;

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
        => await Store.GetAsync(tenantId, (Guid)key) is not null;

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
        => (await Store.QueryAsync(new JobQuery { TenantId = tenantId })).Count;

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
        var owner = Owner();
        await LeaseAsync(owner, TimeSpan.FromMinutes(5));

        (await Store.MarkRunningAsync(job.Id, Owner())).ShouldBeFalse();
        (await Store.MarkRunningAsync(job.Id, owner)).ShouldBeTrue();

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
        await LeaseAsync(Owner(), TimeSpan.FromMinutes(5));

        await Store.CompleteAsync(new JobCompletion
        {
            JobId = job.Id,
            Status = JobStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
        });

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
        await LeaseAsync(Owner(), TimeSpan.FromMinutes(5));

        await Store.ReleaseForRetryAsync(job.Id, "temporary error");

        var current = await Store.GetAsync(job.TenantId, job.Id);
        current!.Status.ShouldBe(JobStatus.Pending);
        current.Attempt.ShouldBe(1);
        current.ErrorMessage.ShouldBe("temporary error");

        (await LeaseAsync(Owner(), TimeSpan.FromMinutes(5))).ShouldNotBeNull();
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
            await Store.CompleteAsync(new JobCompletion { JobId = job.Id, Status = status, CompletedAt = DateTimeOffset.UtcNow });
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

        var leased = await Store.LeaseAsync(Owner(), TimeSpan.FromMinutes(5), ["media"]);

        leased.ShouldNotBeNull();
        leased.Id.ShouldBe(mediaJob.Id);
        leased.Lane.ShouldBe("media");
    }

    [Fact]
    public async Task Lease_with_a_lane_filter_never_returns_a_job_from_an_unlisted_lane()
    {
        await Store.EnqueueAsync(TestData.Job() with { Lane = "default" }, ["a"]);

        (await Store.LeaseAsync(Owner(), TimeSpan.FromMinutes(5), ["media"])).ShouldBeNull();
    }

    [Fact]
    public async Task Lease_with_null_lanes_applies_no_filter()
    {
        await Store.EnqueueAsync(TestData.Job() with { Lane = "media" }, ["a"]);

        (await Store.LeaseAsync(Owner(), TimeSpan.FromMinutes(5), lanes: null)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Lease_with_an_empty_lanes_list_applies_no_filter()
    {
        await Store.EnqueueAsync(TestData.Job() with { Lane = "media" }, ["a"]);

        (await Store.LeaseAsync(Owner(), TimeSpan.FromMinutes(5), lanes: [])).ShouldNotBeNull();
    }

    [Fact]
    public async Task Retry_preserves_the_jobs_lane()
    {
        var job = await Store.EnqueueAsync(TestData.Job() with { Lane = "media" }, ["a"]);
        await LeaseAsync(Owner(), TimeSpan.FromMinutes(5));

        await Store.ReleaseForRetryAsync(job.Id, "temporary error");

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
        var owner = Owner();
        var first = await LeaseAsync(owner, TimeSpan.FromMinutes(5));
        first.ShouldNotBeNull();
        await Store.MarkRunningAsync(first.Id, owner);
        await LeaseAsync(Owner(), TimeSpan.FromMinutes(5));

        var depth = await Store.GetQueueDepthAsync();

        Count(depth, JobLanes.Default, JobStatus.Pending).ShouldBe(1);
        Count(depth, JobLanes.Default, JobStatus.Leased).ShouldBe(1);
        Count(depth, JobLanes.Default, JobStatus.Running).ShouldBe(1);
    }

    [Fact]
    public async Task Queue_depth_never_counts_a_terminal_status()
    {
        var completed = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var cancelled = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        await Store.EnqueueAsync(TestData.Job(), ["a"]);

        await Store.CompleteAsync(new JobCompletion
        {
            JobId = completed.Id,
            Status = JobStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
        });

        await Store.CancelAsync(cancelled.TenantId, cancelled.Id);

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
        var job = await Store.EnqueueAsync(TestData.Job() with { Lane = "media" }, ["a"]);

        await Store.CompleteAsync(new JobCompletion
        {
            JobId = job.Id,
            Status = JobStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
        });

        // A pair with no open jobs is omitted, not reported as zero.
        (await Store.GetQueueDepthAsync()).ShouldNotContain(static row => row.Lane == "media");
    }

    [Fact]
    public async Task Queue_depth_counts_a_job_released_for_retry_as_pending_again()
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        await LeaseAsync(Owner(), TimeSpan.FromMinutes(5));

        await Store.ReleaseForRetryAsync(job.Id, "temporary error");

        var depth = await Store.GetQueueDepthAsync();

        Count(depth, JobLanes.Default, JobStatus.Pending).ShouldBe(1);
        Count(depth, JobLanes.Default, JobStatus.Leased).ShouldBe(0);
    }

    private static long Count(IReadOnlyList<JobQueueDepth> depth, string lane, JobStatus status)
        => depth
            .Where(row => string.Equals(row.Lane, lane, StringComparison.Ordinal) && row.Status == status)
            .Sum(static row => row.Count);

    private ValueTask<JobRecord?> LeaseAsync(string owner, TimeSpan leaseDuration, IReadOnlyList<string>? lanes = null)
        => Store.LeaseAsync(owner, leaseDuration, lanes);

    private static string Owner() => $"worker-{Guid.NewGuid():N}";
}
