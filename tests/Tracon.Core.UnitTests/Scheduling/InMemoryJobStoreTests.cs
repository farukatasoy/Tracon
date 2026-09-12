using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Scheduling;

public sealed class InMemoryJobStoreTests
{
    [Fact]
    public async Task An_enqueued_job_starts_as_pending()
    {
        var store = new InMemoryJobStore();

        var job = await store.EnqueueAsync(NewJob(), ["a", "b", "c"]);

        job.Status.ShouldBe(JobStatus.Pending);
        job.TotalItems.ShouldBe(3);
        job.Attempt.ShouldBe(0);

        var items = await store.ListItemsAsync(job.Id);
        items.Count.ShouldBe(3);
        items.Select(static i => i.Seq).ShouldBe([0, 1, 2]);
        items.ShouldAllBe(static i => i.Status == JobItemStatus.Pending);
    }

    [Fact]
    public async Task A_job_that_is_not_yet_due_cannot_be_leased()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var store = new InMemoryJobStore(clock);

        await store.EnqueueAsync(
            NewJob() with { ScheduledFor = clock.GetUtcNow() + TimeSpan.FromMinutes(5) },
            ["a"]);

        (await store.LeaseAsync("worker-1", TimeSpan.FromMinutes(5), lanes: null)).ShouldBeNull();
    }

    [Fact]
    public async Task Leasing_sets_the_status_to_Leased_and_increments_the_attempt()
    {
        var store = new InMemoryJobStore();
        var job = await store.EnqueueAsync(NewJob(), ["a"]);

        var leased = await store.LeaseAsync("worker-1", TimeSpan.FromMinutes(5), lanes: null);

        leased.ShouldNotBeNull();
        leased.Id.ShouldBe(job.Id);
        leased.Status.ShouldBe(JobStatus.Leased);
        leased.LeaseOwner.ShouldBe("worker-1");
        leased.Attempt.ShouldBe(1);
    }

    [Fact]
    public async Task A_leased_job_is_not_given_to_another_worker_before_the_lease_expires()
    {
        var store = new InMemoryJobStore();
        await store.EnqueueAsync(NewJob(), ["a"]);

        await store.LeaseAsync("worker-1", TimeSpan.FromMinutes(5), lanes: null);

        (await store.LeaseAsync("worker-2", TimeSpan.FromMinutes(5), lanes: null)).ShouldBeNull();
    }

    [Fact]
    public async Task A_job_can_be_re_leased_once_the_lease_expires()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var store = new InMemoryJobStore(clock);
        var job = await store.EnqueueAsync(NewJob() with { ScheduledFor = clock.GetUtcNow() }, ["a"]);

        await store.LeaseAsync("worker-1", TimeSpan.FromMinutes(5), lanes: null);
        clock.Advance(TimeSpan.FromMinutes(6));

        var reclaimed = await store.LeaseAsync("worker-2", TimeSpan.FromMinutes(5), lanes: null);

        reclaimed.ShouldNotBeNull();
        reclaimed.Id.ShouldBe(job.Id);
        reclaimed.LeaseOwner.ShouldBe("worker-2");
        reclaimed.Attempt.ShouldBe(2);
    }

    [Fact]
    public async Task MarkRunningAsync_works_only_for_the_actual_owner()
    {
        var store = new InMemoryJobStore();
        var job = await store.EnqueueAsync(NewJob(), ["a"]);
        await store.LeaseAsync("worker-1", TimeSpan.FromMinutes(5), lanes: null);

        (await store.MarkRunningAsync(job.Id, "wrong-worker")).ShouldBeFalse();
        (await store.MarkRunningAsync(job.Id, "worker-1")).ShouldBeTrue();

        var current = await store.GetAsync("tenant", job.Id);
        current!.Status.ShouldBe(JobStatus.Running);
    }

    [Fact]
    public async Task ReportItemAsync_is_idempotent()
    {
        var store = new InMemoryJobStore();
        var job = await store.EnqueueAsync(NewJob(), ["a", "b"]);

        var result = new JobItemResult { JobId = job.Id, Seq = 0, Status = JobItemStatus.Completed, RunId = Guid.NewGuid() };

        await store.ReportItemAsync(result);
        await store.ReportItemAsync(result); // scenario: lease expired and the item is reported again

        var current = await store.GetAsync("tenant", job.Id);
        current!.DoneItems.ShouldBe(1);
        current.FailedItems.ShouldBe(0);
    }

    [Fact]
    public async Task ReportItemAsync_counts_a_failed_item_separately()
    {
        var store = new InMemoryJobStore();
        var job = await store.EnqueueAsync(NewJob(), ["a", "b"]);

        await store.ReportItemAsync(new JobItemResult { JobId = job.Id, Seq = 0, Status = JobItemStatus.Completed });
        await store.ReportItemAsync(new JobItemResult { JobId = job.Id, Seq = 1, Status = JobItemStatus.Failed, Error = "boom" });

        var current = await store.GetAsync("tenant", job.Id);
        current!.DoneItems.ShouldBe(1);
        current.FailedItems.ShouldBe(1);

        var items = await store.ListItemsAsync(job.Id);
        items.Single(static i => i.Seq == 1).Error.ShouldBe("boom");
    }

    [Fact]
    public async Task CompleteAsync_releases_the_lease()
    {
        var clock = new ManualTimeProvider();
        var store = new InMemoryJobStore(clock);
        var job = await store.EnqueueAsync(NewJob(), ["a"]);
        await store.LeaseAsync("worker-1", TimeSpan.FromMinutes(5), lanes: null);

        await store.CompleteAsync(new JobCompletion
        {
            JobId = job.Id,
            Status = JobStatus.Completed,
            CompletedAt = clock.GetUtcNow(),
        });

        var current = await store.GetAsync("tenant", job.Id);
        current!.Status.ShouldBe(JobStatus.Completed);
        current.LeaseOwner.ShouldBeNull();
        current.LeaseUntil.ShouldBeNull();
    }

    [Fact]
    public async Task ReleaseForRetryAsync_returns_to_pending_without_resetting_the_attempt()
    {
        var store = new InMemoryJobStore();
        var job = await store.EnqueueAsync(NewJob(), ["a"]);
        await store.LeaseAsync("worker-1", TimeSpan.FromMinutes(5), lanes: null);

        await store.ReleaseForRetryAsync(job.Id, "temporary error");

        var current = await store.GetAsync("tenant", job.Id);
        current!.Status.ShouldBe(JobStatus.Pending);
        current.Attempt.ShouldBe(1); // LeaseAsync already incremented it; unchanged here.
        current.ErrorMessage.ShouldBe("temporary error");

        (await store.LeaseAsync("worker-2", TimeSpan.FromMinutes(5), lanes: null)).ShouldNotBeNull();
    }

    [Theory]
    [InlineData(JobStatus.Pending, true)]
    [InlineData(JobStatus.Leased, true)]
    [InlineData(JobStatus.Running, true)]
    [InlineData(JobStatus.Completed, false)]
    [InlineData(JobStatus.Failed, false)]
    [InlineData(JobStatus.Cancelled, false)]
    public async Task CancelAsync_works_only_in_eligible_statuses(JobStatus status, bool expected)
    {
        var store = new InMemoryJobStore();
        var job = await store.EnqueueAsync(NewJob(), ["a"]);

        // CompleteAsync/LeaseAsync are used to set up the status indirectly.
        if (status is JobStatus.Completed or JobStatus.Failed)
        {
            await store.CompleteAsync(new JobCompletion { JobId = job.Id, Status = status, CompletedAt = DateTimeOffset.UtcNow });
        }
        else if (status == JobStatus.Cancelled)
        {
            await store.CancelAsync("tenant", job.Id);
        }
        else if (status is JobStatus.Leased or JobStatus.Running)
        {
            await store.LeaseAsync("worker-1", TimeSpan.FromMinutes(5), lanes: null);

            if (status == JobStatus.Running)
            {
                await store.MarkRunningAsync(job.Id, "worker-1");
            }
        }

        (await store.CancelAsync("tenant", job.Id)).ShouldBe(expected);
    }

    [Fact]
    public async Task CancelAsync_does_not_affect_another_tenants_job()
    {
        var store = new InMemoryJobStore();
        var job = await store.EnqueueAsync(NewJob() with { TenantId = "tenant-a" }, ["a"]);

        (await store.CancelAsync("tenant-b", job.Id)).ShouldBeFalse();

        var current = await store.GetAsync("tenant-a", job.Id);
        current!.Status.ShouldBe(JobStatus.Pending);
    }

    [Fact]
    public async Task QueryAsync_filters_by_tenant()
    {
        var store = new InMemoryJobStore();
        await store.EnqueueAsync(NewJob() with { TenantId = "a" }, ["x"]);
        await store.EnqueueAsync(NewJob() with { TenantId = "b" }, ["x"]);

        var results = await store.QueryAsync(new JobQuery { TenantId = "a" });

        results.ShouldHaveSingleItem();
        results[0].TenantId.ShouldBe("a");
    }

    private static JobRecord NewJob()
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant",
            HandlerKey = JobHandlerKeys.AgentBatch,
            TargetName = "summarizer",
            Status = JobStatus.Pending,
            ScheduledFor = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
