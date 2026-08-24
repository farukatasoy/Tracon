
namespace AgentPrism.Testing.Contracts.Storage;

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

        (await Store.LeaseAsync(Owner(), TimeSpan.FromMinutes(5))).ShouldBeNull();
    }

    [Fact]
    public async Task Leasing_marks_Leased_and_increments_the_attempt()
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var owner = Owner();

        var leased = await Store.LeaseAsync(owner, TimeSpan.FromMinutes(5));

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
        await Store.LeaseAsync(Owner(), TimeSpan.FromMinutes(5));

        (await Store.LeaseAsync(Owner(), TimeSpan.FromMinutes(5))).ShouldBeNull();
    }

    [Fact]
    public async Task Job_can_be_re_leased_once_the_lease_expires()
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);

        await Store.LeaseAsync(Owner(), TimeSpan.FromMilliseconds(20));
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        var reclaimed = await Store.LeaseAsync(Owner(), TimeSpan.FromMinutes(5));

        reclaimed.ShouldNotBeNull();
        reclaimed.Id.ShouldBe(job.Id);
        reclaimed.Attempt.ShouldBe(2);
    }

    [Fact]
    public async Task MarkRunningAsync_works_only_for_the_real_owner()
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var owner = Owner();
        await Store.LeaseAsync(owner, TimeSpan.FromMinutes(5));

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
        await Store.LeaseAsync(Owner(), TimeSpan.FromMinutes(5));

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
        await Store.LeaseAsync(Owner(), TimeSpan.FromMinutes(5));

        await Store.ReleaseForRetryAsync(job.Id, "temporary error");

        var current = await Store.GetAsync(job.TenantId, job.Id);
        current!.Status.ShouldBe(JobStatus.Pending);
        current.Attempt.ShouldBe(1);
        current.ErrorMessage.ShouldBe("temporary error");

        (await Store.LeaseAsync(Owner(), TimeSpan.FromMinutes(5))).ShouldNotBeNull();
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

    private static string Owner() => $"worker-{Guid.NewGuid():N}";
}
