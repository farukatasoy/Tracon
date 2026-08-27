using AgentPrism.Core.UnitTests.Fakes;

namespace AgentPrism.Core.UnitTests.Scheduling;

/// <summary>
/// Measures the runtime behavior <see cref="IJobHandler"/>'s at-least-once
/// contract relies on: that an abandoned lease really does expire and free
/// the job for a new attempt, and that the re-leased job's item list still
/// carries the item(s) an earlier, now-dead attempt already reported --
/// unfiltered, exactly as <see cref="JobContext.Items"/>'s documentation
/// describes. A doc comment describing this without a test measuring it
/// would give a runtime guarantee the store was never proven to keep.
/// </summary>
public sealed class JobLeaseExpiryTests
{
    [Fact]
    public async Task An_abandoned_lease_expires_and_the_re_leased_job_carries_its_completed_item_unfiltered()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var store = new InMemoryJobStore(clock);

        var job = await store.EnqueueAsync(
            new JobRecord
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant",
                Kind = JobKind.AgentBatch,
                TargetName = "summarizer",
                Status = JobStatus.Pending,
                ScheduledFor = clock.GetUtcNow(),
                CreatedAt = clock.GetUtcNow(),
            },
            ["first", "second"]);

        var leaseDuration = TimeSpan.FromMinutes(5);

        var firstLease = await store.LeaseAsync("worker-crashed", leaseDuration);
        firstLease.ShouldNotBeNull();
        await store.MarkRunningAsync(job.Id, "worker-crashed");

        // The crashed worker manages to report the first item before dying --
        // this is exactly the state a real process crash, or a lease that
        // simply expires mid-handler, leaves behind. No CompleteAsync or
        // ReleaseForRetryAsync call follows: the worker is gone.
        await store.ReportItemAsync(new JobItemResult
        {
            JobId = job.Id,
            Seq = 0,
            Status = JobItemStatus.Completed,
            RunId = Guid.NewGuid(),
        });

        clock.Advance(leaseDuration + TimeSpan.FromSeconds(1));

        var secondLease = await store.LeaseAsync("worker-recovered", leaseDuration);

        secondLease.ShouldNotBeNull("the lease must actually expire and free the job for a new attempt");
        secondLease!.Id.ShouldBe(job.Id);
        secondLease.Attempt.ShouldBe(2);

        var items = await store.ListItemsAsync(job.Id);

        items.Count.ShouldBe(2);
        items.Single(static item => item.Seq == 0).Status.ShouldBe(JobItemStatus.Completed);
        items.Single(static item => item.Seq == 1).Status.ShouldBe(JobItemStatus.Pending);
    }
}
