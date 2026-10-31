namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// <see cref="JobLease"/> is the value every lease-bound store write is fenced by. A lease with an empty owner or no
/// attempt would match no job in some stores and every job in others, so it cannot be built.
/// </summary>
public sealed class JobLeaseTests
{
    [Fact]
    public void A_lease_is_taken_from_the_record_the_store_returned()
    {
        var job = Leased(owner: "worker-1", attempt: 3);

        var lease = JobLease.Of(job);

        lease.JobId.ShouldBe(job.Id);
        lease.Owner.ShouldBe("worker-1");
        lease.Attempt.ShouldBe(3);
    }

    [Fact]
    public void Two_leases_of_the_same_job_and_owner_differ_by_attempt()
    {
        var job = Leased(owner: "worker-1", attempt: 1);

        JobLease.Of(job).ShouldBe(JobLease.Of(job));
        JobLease.Of(job).ShouldNotBe(JobLease.Of(job with { Attempt = 2 }));
    }

    [Fact]
    public void A_record_that_is_not_leased_gives_no_lease()
    {
        var job = Leased(owner: null, attempt: 0);

        Should.Throw<ArgumentException>(() => JobLease.Of(job));
    }

    [Fact]
    public void A_leased_record_without_an_attempt_gives_no_lease()
        => Should.Throw<ArgumentOutOfRangeException>(() => JobLease.Of(Leased(owner: "worker-1", attempt: 0)));

    [Fact]
    public void No_record_gives_no_lease()
        => Should.Throw<ArgumentNullException>(() => JobLease.Of(null!));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_owner_with_no_text_is_refused(string owner)
        => Should.Throw<ArgumentException>(() => new JobLease { JobId = Guid.NewGuid(), Owner = owner, Attempt = 1 });

    [Fact]
    public void A_null_owner_is_refused()
        => Should.Throw<ArgumentNullException>(() => new JobLease { JobId = Guid.NewGuid(), Owner = null!, Attempt = 1 });

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void An_attempt_below_one_is_refused(int attempt)
        => Should.Throw<ArgumentOutOfRangeException>(() => new JobLease { JobId = Guid.NewGuid(), Owner = "worker-1", Attempt = attempt });

    [Fact]
    public void Changing_a_lease_goes_through_the_same_checks()
    {
        var lease = new JobLease { JobId = Guid.NewGuid(), Owner = "worker-1", Attempt = 1 };

        Should.Throw<ArgumentOutOfRangeException>(() => lease with { Attempt = 0 });
        Should.Throw<ArgumentException>(() => lease with { Owner = " " });
    }

    private static JobRecord Leased(string? owner, int attempt)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant",
            HandlerKey = JobHandlerKeys.AgentBatch,
            TargetName = "summarizer",
            Status = JobStatus.Leased,
            LeaseOwner = owner,
            Attempt = attempt,
            ScheduledFor = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
