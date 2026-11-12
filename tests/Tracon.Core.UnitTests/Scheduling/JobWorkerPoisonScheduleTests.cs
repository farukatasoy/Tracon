using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// One schedule row the worker cannot compute a next run for does not stop the schedules behind it.
/// </summary>
/// <remarks>
/// A row reaches the store through the management API, which refuses a cron expression that never fires, and through
/// <see cref="IJobScheduleStore"/>, which takes whatever it is given. The worker reads the due rows of every tenant in
/// one list and walks it in order, so a row it cannot advance must cost the installation that row and no other: the
/// schedules of other tenants sit behind it in the same list.
/// </remarks>
public sealed class JobWorkerPoisonScheduleTests
{
    /// <summary>February 30th: parses, because each field is judged alone, and never fires.</summary>
    private const string NeverFires = "0 0 30 2 *";

    [Fact]
    public async Task A_schedule_that_never_fires_does_not_keep_the_next_tenants_schedule_from_running()
    {
        var jobs = new InMemoryJobStore();
        var inner = new InMemoryJobScheduleStore();
        var schedules = new PoisonedFirstScheduleStore(inner, "poisoned");
        var handler = new ImmediateHandler();
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));
        var now = DateTimeOffset.UtcNow;

        await schedules.SaveAsync(Schedule("tenant-a", "poisoned", NeverFires, now), TestContext.Current.CancellationToken);
        await schedules.SaveAsync(Schedule("tenant-b", "healthy", "*/5 * * * *", now), TestContext.Current.CancellationToken);

        using var worker = new JobWorkerBackgroundService(
            jobs,
            schedules,
            host.Registry,
            host.Scopes,
            new StaticOptionsMonitor<TraconSchedulingOptions>(new TraconSchedulingOptions
            {
                MaxConcurrentJobs = 1,
                PollInterval = TimeSpan.FromMilliseconds(5),
            }),
            new SchemaReadyGate([]),
            new NotDraining(),
            logger: NullLogger<JobWorkerBackgroundService>.Instance);

        await worker.StartAsync(TestContext.Current.CancellationToken);

        try
        {
            var healthyJobs = await WaitUntil.ValueAsync(
                () => jobs.QueryAsync(new JobQuery { TenantId = "tenant-b" }, TestContext.Current.CancellationToken).AsTask(),
                static found => found.Count > 0,
                "the healthy tenant's schedule to dispatch a job while another tenant's schedule can never fire");

            healthyJobs.ShouldNotBeEmpty();
        }
        finally
        {
            await worker.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task A_schedule_claim_that_never_answers_is_given_up_and_does_not_keep_the_next_tenants_schedule_from_running()
    {
        // The claim of one row hung, and the loop that dispatches every tenant's rows waited with it: the schedules behind
        // it stopped for as long as the store took. The call is bounded by one lease duration, as the lease calls are.
        var leaseDuration = TimeSpan.FromMinutes(5);
        var poll = TimeSpan.FromSeconds(30);
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var jobs = new InMemoryJobStore();
        var schedules = new HangingClaimScheduleStore(new InMemoryJobScheduleStore(), "hanging");
        var now = clock.GetUtcNow();

        await schedules.SaveAsync(Schedule("tenant-a", "hanging", "*/5 * * * *", now), TestContext.Current.CancellationToken);
        await schedules.SaveAsync(Schedule("tenant-b", "healthy", "*/5 * * * *", now), TestContext.Current.CancellationToken);

        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, new ImmediateHandler()));
        using var worker = new JobWorkerBackgroundService(
            jobs,
            schedules,
            host.Registry,
            host.Scopes,
            new StaticOptionsMonitor<TraconSchedulingOptions>(new TraconSchedulingOptions
            {
                MaxConcurrentJobs = 1,
                PollInterval = poll,
                LeaseDuration = leaseDuration,
            }),
            new SchemaReadyGate([]),
            new NotDraining(),
            timeProvider: clock,
            logger: NullLogger<JobWorkerBackgroundService>.Instance);

        await worker.StartAsync(TestContext.Current.CancellationToken);

        try
        {
            await WaitUntil.TrueAsync(() => clock.Pending.Contains(poll), "the worker to arm its poll timer");

            clock.Advance(poll);
            await WaitUntil.TrueAsync(() => schedules.HangingClaims > 0, "the worker to reach the claim that hangs");

            // The budget is spent: the claim is given up and the healthy row, which comes after it, is dispatched.
            clock.Advance(leaseDuration);

            var healthyJobs = await WaitUntil.ValueAsync(
                () => jobs.QueryAsync(new JobQuery { TenantId = "tenant-b" }, TestContext.Current.CancellationToken).AsTask(),
                static found => found.Count > 0,
                "the healthy tenant's schedule to dispatch a job after the hung claim was given up");

            healthyJobs.ShouldNotBeEmpty();
        }
        finally
        {
            await worker.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    private static JobSchedule Schedule(string tenantId, string name, string cron, DateTimeOffset now)
        => new()
        {
            TenantId = tenantId,
            Name = name,
            HandlerKey = JobHandlerKeys.AgentBatch,
            TargetName = "summarizer",
            Cron = cron,
            Enabled = true,
            NextRunAt = now.AddMinutes(-1),
            CreatedAt = now,
            UpdatedAt = now,
        };

    /// <summary>
    /// Hands the worker the due list with one named row first. A store gives no order, and the order is the whole
    /// scenario: the rows behind the poisoned one are the ones at risk.
    /// </summary>
    private sealed class PoisonedFirstScheduleStore(IJobScheduleStore inner, string poisonedName) : IJobScheduleStore
    {
        public ValueTask<JobSchedule?> GetAsync(string tenantId, string name, CancellationToken cancellationToken = default)
            => inner.GetAsync(tenantId, name, cancellationToken);

        public ValueTask<IReadOnlyList<JobSchedule>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
            => inner.ListAsync(tenantId, cancellationToken);

        public ValueTask<JobSchedule> SaveAsync(JobSchedule schedule, CancellationToken cancellationToken = default)
            => inner.SaveAsync(schedule, cancellationToken);

        public ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default)
            => inner.DeleteAsync(tenantId, name, cancellationToken);

        public async ValueTask<IReadOnlyList<JobSchedule>> ListDueAsync(DateTimeOffset asOfUtc, CancellationToken cancellationToken = default)
            => [.. (await inner.ListDueAsync(asOfUtc, cancellationToken).ConfigureAwait(false))
                .OrderBy(schedule => string.Equals(schedule.Name, poisonedName, StringComparison.Ordinal) ? 0 : 1)];

        public ValueTask<bool> TryClaimNextRunAsync(
            Guid scheduleId,
            DateTimeOffset expectedNextRunAt,
            DateTimeOffset newNextRunAt,
            DateTimeOffset ranAt,
            CancellationToken cancellationToken = default)
            => inner.TryClaimNextRunAsync(scheduleId, expectedNextRunAt, newNextRunAt, ranAt, cancellationToken);
    }

    /// <summary>
    /// Hands the worker the due list with one named row first and never answers the claim of that row.
    /// </summary>
    private sealed class HangingClaimScheduleStore(IJobScheduleStore inner, string hangingName) : IJobScheduleStore
    {
        private int _hangingClaims;

        public int HangingClaims => Volatile.Read(ref _hangingClaims);

        public ValueTask<JobSchedule?> GetAsync(string tenantId, string name, CancellationToken cancellationToken = default)
            => inner.GetAsync(tenantId, name, cancellationToken);

        public ValueTask<IReadOnlyList<JobSchedule>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
            => inner.ListAsync(tenantId, cancellationToken);

        public ValueTask<JobSchedule> SaveAsync(JobSchedule schedule, CancellationToken cancellationToken = default)
            => inner.SaveAsync(schedule, cancellationToken);

        public ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default)
            => inner.DeleteAsync(tenantId, name, cancellationToken);

        public async ValueTask<IReadOnlyList<JobSchedule>> ListDueAsync(DateTimeOffset asOfUtc, CancellationToken cancellationToken = default)
            => [.. (await inner.ListDueAsync(asOfUtc, cancellationToken).ConfigureAwait(false))
                .OrderBy(schedule => string.Equals(schedule.Name, hangingName, StringComparison.Ordinal) ? 0 : 1)];

        public async ValueTask<bool> TryClaimNextRunAsync(
            Guid scheduleId,
            DateTimeOffset expectedNextRunAt,
            DateTimeOffset newNextRunAt,
            DateTimeOffset ranAt,
            CancellationToken cancellationToken = default)
        {
            var schedule = (await inner.ListDueAsync(ranAt, cancellationToken).ConfigureAwait(false)).FirstOrDefault(row => row.Id == scheduleId);

            if (schedule is not null && string.Equals(schedule.Name, hangingName, StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _hangingClaims);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); // delay: simulated
            }

            return await inner.TryClaimNextRunAsync(scheduleId, expectedNextRunAt, newNextRunAt, ranAt, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class NotDraining : ITraconDrainState
    {
        public bool IsDraining => false;
    }

    private sealed class ImmediateHandler : IJobHandler
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default) => default;
    }
}
