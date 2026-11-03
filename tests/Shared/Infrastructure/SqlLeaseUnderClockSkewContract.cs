using Microsoft.Extensions.Options;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.Tests.Common;

/// <summary>The stores of two nodes over one schema.</summary>
/// <param name="FirstLeases">The first node's single-executor lease store.</param>
/// <param name="SecondLeases">The second node's single-executor lease store.</param>
/// <param name="FirstJobs">The first node's job store.</param>
/// <param name="SecondJobs">The second node's job store.</param>
public sealed record SkewedStores(
    ISingletonLeaseStore FirstLeases,
    ISingletonLeaseStore SecondLeases,
    IJobStore FirstJobs,
    IJobStore SecondJobs);

/// <summary>
/// What the SQL lease stores do when the two nodes that share one database disagree about the time, written once for
/// every SQL provider.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 <strong>This is a measurement, not a support statement.</strong> A lease row carries the instant the writing node
/// computed from its OWN clock (<c>expires_at</c> for the single-executor lease, <c>lease_until</c> for a job), and every
/// reading node compares that instant with ITS clock. Two nodes whose clocks differ therefore disagree about when a
/// lease ends, and the database server's own clock is never consulted. The scenarios below give the two nodes two
/// manual clocks over one schema and record what each sees.
/// </para>
/// <para>
/// What it proves is the shape of the behaviour, not a real network: the clocks here differ by an exact amount from the
/// first step, while real machines drift and are corrected in steps. It does not measure NTP, and the numbers hold for
/// the lease lengths used here (30 seconds for the single-executor lease, five minutes for a job: the shipped job
/// default). The single-executor lease in a deployment is 60 seconds by default.
/// </para>
/// <para>
/// A provider's test project derives from this class and says one thing: how to open stores over one migrated, empty
/// schema, the first pair reading <c>firstClock</c> and the second pair reading <c>secondClock</c>.
/// </para>
/// </remarks>
public abstract class SqlLeaseUnderClockSkewContract
{
    private static readonly TimeSpan SingletonLeaseDuration = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan JobLeaseDuration = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan Step = TimeSpan.FromSeconds(1);

    /// <summary>Opens the stores of two nodes over one migrated, empty schema.</summary>
    /// <param name="firstClock">The clock the first node's stores read.</param>
    /// <param name="secondClock">The clock the second node's stores read.</param>
    /// <returns>The stores.</returns>
    protected abstract ValueTask<SkewedStores> OpenAsync(TimeProvider firstClock, TimeProvider secondClock);

    /// <summary>
    /// A node whose clock runs ahead takes a LIVE single-executor lease once it is ahead by more than the margin the
    /// owner's renewals leave, and the owner learns of it at its next renewal.
    /// </summary>
    /// <remarks>
    /// The owner renews every third of the lease, so on a steady lease the other node sees at most two thirds of the
    /// lease left, not all of it. The second node polls every second, which is the most hostile phase it could have.
    /// </remarks>
    /// <param name="aheadSeconds">How far the second node's clock is ahead of the owner's.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(25)]
    [InlineData(31)]
    public async Task A_node_whose_clock_runs_ahead_takes_a_live_single_executor_lease_past_the_margin(int aheadSeconds)
    {
        var owner = new ManualClock();
        var other = new ManualClock();
        other.Advance(TimeSpan.FromSeconds(aheadSeconds));

        var stores = await OpenAsync(owner, other);
        var options = Options();
        var name = $"skew-{Guid.NewGuid():N}";

        var ownerGuard = new SingletonGuard(stores.FirstLeases, options, name, timeProvider: owner);
        var otherGuard = new SingletonGuard(stores.SecondLeases, options, name, timeProvider: other);
        var renewEvery = (int)SingletonGuard.ComputeRenewInterval(SingletonLeaseDuration).TotalSeconds;

        int? otherTookAt = null;
        int? ownerLostAt = null;

        for (var second = 0; second <= 60; second++)
        {
            if (second > 0)
            {
                owner.Advance(Step);
                other.Advance(Step);
            }

            if (second % renewEvery == 0)
            {
                await ownerGuard.TickAsync(TestContext.Current.CancellationToken);
            }

            await otherGuard.TickAsync(TestContext.Current.CancellationToken);

            if (otherTookAt is null && otherGuard.IsHeld)
            {
                otherTookAt = second;
            }

            if (otherTookAt is not null && ownerLostAt is null && !ownerGuard.IsHeld)
            {
                ownerLostAt = second;
            }
        }

        int? bothHeldFor = otherTookAt is { } tookAt && ownerLostAt is { } lostAt ? lostAt - tookAt : null;

        Report(
            "single-executor lease",
            $"second node ahead by {aheadSeconds}s",
            $"took the live lease at {Describe(otherTookAt)}; owner noticed at {Describe(ownerLostAt)}; both believed they were the executor for {Describe(bothHeldFor)}");

        if (aheadSeconds <= SingletonMargin(renewEvery))
        {
            otherTookAt.ShouldBeNull($"{aheadSeconds}s ahead is inside the margin the owner's renewals leave");
            ownerLostAt.ShouldBeNull("the owner keeps what it holds");

            return;
        }

        otherTookAt.ShouldNotBeNull($"{aheadSeconds}s ahead is past the margin: the lease reads as over");
        ownerLostAt.ShouldNotBeNull("the owner learns of the take-over at a renewal");

        // The window is bounded by the owner's renewal interval and by nothing else.
        bothHeldFor.ShouldNotBeNull();
        bothHeldFor.Value.ShouldBeLessThanOrEqualTo(renewEvery);

        if (aheadSeconds > SingletonLeaseDuration.TotalSeconds)
        {
            otherTookAt.Value.ShouldBe(0, "ahead by more than the whole lease: a freshly written lease is already over");
            bothHeldFor.Value.ShouldBe(renewEvery, "the owner renews only every third of the lease");
        }
    }

    /// <summary>
    /// The owner's clock running ahead costs the other nodes time instead: a lease written with a fast clock reads as
    /// live for the lease plus the difference after the owner stopped renewing it.
    /// </summary>
    /// <param name="aheadSeconds">How far the owner's clock is ahead of the other node's.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(31)]
    public async Task A_lease_written_with_a_fast_clock_stays_live_longer_for_every_other_node(int aheadSeconds)
    {
        var owner = new ManualClock();
        owner.Advance(TimeSpan.FromSeconds(aheadSeconds));
        var other = new ManualClock();

        var stores = await OpenAsync(owner, other);
        var name = $"skew-{Guid.NewGuid():N}";

        (await stores.FirstLeases.TryAcquireAsync(name, "owner", SingletonLeaseDuration, TestContext.Current.CancellationToken))
            .ShouldBeTrue();

        // The owner stops here, as a crashed node does. The other node polls on every second of ITS clock.
        int? tookAt = null;

        for (var second = 1; second <= 120 && tookAt is null; second++)
        {
            other.Advance(Step);

            if (await stores.SecondLeases.TryAcquireAsync(name, "other", SingletonLeaseDuration, TestContext.Current.CancellationToken))
            {
                tookAt = second;
            }
        }

        Report(
            "single-executor lease",
            $"owner ahead by {aheadSeconds}s, then stopped",
            $"the other node took the lease {Describe(tookAt)} after the owner's last write (the lease is {SingletonLeaseDuration.TotalSeconds}s)");

        tookAt.ShouldNotBeNull();
        tookAt.Value.ShouldBe((int)SingletonLeaseDuration.TotalSeconds + aheadSeconds + 1, "the lease ends at the writer's clock plus the lease");
    }

    /// <summary>
    /// A node whose clock runs ahead by more than a job's lease takes a job another node is RUNNING, and the owner's
    /// own calls are then refused.
    /// </summary>
    /// <param name="aheadSeconds">How far the second node's clock is ahead of the first node's.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(150)]
    [InlineData(301)]
    public async Task A_node_whose_clock_runs_ahead_takes_a_job_another_node_is_running_once_it_is_ahead_by_the_lease(int aheadSeconds)
    {
        var first = new ManualClock();
        var second = new ManualClock();
        second.Advance(TimeSpan.FromSeconds(aheadSeconds));

        var stores = await OpenAsync(first, second);

        await stores.FirstJobs.EnqueueAsync(TestData.Job(scheduledFor: first.GetUtcNow()), ["a"], TestContext.Current.CancellationToken);

        var leased = (await stores.FirstJobs.LeaseAsync(Request("first"), TestContext.Current.CancellationToken)).ShouldNotBeNull();
        var firstLease = JobLease.Of(leased);

        (await stores.FirstJobs.MarkRunningAsync(firstLease, TestContext.Current.CancellationToken)).ShouldBeTrue();

        var taken = await stores.SecondJobs.LeaseAsync(Request("second"), TestContext.Current.CancellationToken);

        var renewed = await stores.FirstJobs.RenewLeaseAsync(firstLease, JobLeaseDuration, TestContext.Current.CancellationToken);
        var completedByFirst = await stores.FirstJobs.CompleteAsync(firstLease, Completion(first), TestContext.Current.CancellationToken);

        var completedBySecond = taken is null
            ? (bool?)null
            : await stores.SecondJobs.CompleteAsync(JobLease.Of(taken), Completion(second), TestContext.Current.CancellationToken);

        Report(
            "job lease",
            $"second node ahead by {aheadSeconds}s, job is Running on the first node",
            $"second node {(taken is null ? "did not take the job" : $"took it (attempt {taken.Attempt})")}; " +
            $"first node's renewal {Describe(renewed)}, completion {Describe(completedByFirst)}; second node's completion {Describe(completedBySecond)}");

        if (aheadSeconds <= JobLeaseDuration.TotalSeconds)
        {
            taken.ShouldBeNull($"{aheadSeconds}s ahead is inside the lease");
            renewed.ShouldBeTrue();
            completedByFirst.ShouldBeTrue();

            return;
        }

        var stolen = taken.ShouldNotBeNull($"{aheadSeconds}s ahead is past the lease: a running job reads as abandoned");

        stolen.Attempt.ShouldBe(2, "the take-over counts as another attempt");
        renewed.ShouldBeFalse("the lease belongs to the second node now");
        completedByFirst.ShouldBeFalse("the first node's result is refused: the take-over fences it out");
        completedBySecond.ShouldBe(true);

        // The queue's own limit for an agent run that was queued is one attempt: the worker that takes the job over
        // fails it without running it, while the first node is still working on it.
        stolen.Attempt.ShouldBeGreaterThan(new TraconAsyncRunOptions().MaxAttempts);
    }

    /// <summary>
    /// A node whose clock runs ahead past the lease takes EVERY running job on its next polls, one per poll, and does not
    /// take its own again.
    /// </summary>
    [Fact]
    public async Task A_fast_node_polling_takes_every_running_job_of_another_node_once()
    {
        var first = new ManualClock();
        var second = new ManualClock();
        second.Advance(JobLeaseDuration + TimeSpan.FromSeconds(1));

        var stores = await OpenAsync(first, second);
        const int Jobs = 3;

        for (var index = 0; index < Jobs; index++)
        {
            await stores.FirstJobs.EnqueueAsync(TestData.Job(scheduledFor: first.GetUtcNow()), [$"item-{index}"], TestContext.Current.CancellationToken);

            var leased = (await stores.FirstJobs.LeaseAsync(Request("first"), TestContext.Current.CancellationToken)).ShouldNotBeNull();

            (await stores.FirstJobs.MarkRunningAsync(JobLease.Of(leased), TestContext.Current.CancellationToken)).ShouldBeTrue();
        }

        var attempts = new List<int>();

        for (var poll = 0; poll < Jobs + 2; poll++)
        {
            second.Advance(Step);

            if (await stores.SecondJobs.LeaseAsync(Request("second"), TestContext.Current.CancellationToken) is { } taken)
            {
                attempts.Add(taken.Attempt);
            }
        }

        // The slow node polling again takes nothing: the leases the fast node wrote end in the future on its clock.
        first.Advance(Step);
        var takenBack = await stores.FirstJobs.LeaseAsync(Request("first"), TestContext.Current.CancellationToken);

        Report(
            "job lease",
            $"second node ahead by {JobLeaseDuration.TotalSeconds + 1}s polls {Jobs + 2} times, {Jobs} jobs Running on the first node",
            $"took {attempts.Count} jobs, attempts [{string.Join(", ", attempts)}]; the first node took {(takenBack is null ? "nothing" : "a job")} back");

        attempts.ShouldBe([2, 2, 2], "each running job is taken once, as a second attempt");
        takenBack.ShouldBeNull();
    }

    private static int SingletonMargin(int renewEverySeconds)
        => (int)SingletonLeaseDuration.TotalSeconds - renewEverySeconds;

    private static JobLeaseRequest Request(string owner)
        => new() { Owner = owner, LeaseDuration = JobLeaseDuration };

    private static JobCompletion Completion(TimeProvider clock)
        => new() { Status = JobStatus.Completed, CompletedAt = clock.GetUtcNow() };

    private static StaticOptionsMonitor<SingletonExecutionOptions> Options()
        => new(new SingletonExecutionOptions { Enabled = true, LeaseDuration = SingletonLeaseDuration });

    private static string Describe(int? seconds) => seconds is { } value ? $"t+{value}s" : "never";

    private static string Describe(bool value) => value ? "accepted" : "refused";

    private static string Describe(bool? value) => value is { } known ? Describe(known) : "n/a";

    /// <summary>Writes one measured row where a run with detailed output shows it.</summary>
    private static void Report(string subject, string setup, string observed)
        => TestContext.Current.TestOutputHelper?.WriteLine($"[clock skew] {subject} | {setup} | {observed}");

    /// <summary>An options monitor that returns one value and never tracks changes.</summary>
    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
        where T : class
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
