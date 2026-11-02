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

    private sealed class NotDraining : ITraconDrainState
    {
        public bool IsDraining => false;
    }

    private sealed class ImmediateHandler : IJobHandler
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default) => default;
    }
}
