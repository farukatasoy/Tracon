using Microsoft.Extensions.DependencyInjection;
using Tracon.Sqlite.IntegrationTests.Infrastructure;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// The clock a host registers with the container is the clock the SQLite run store ages a heartbeat with.
/// </summary>
/// <remarks>
/// <para>
/// The heartbeat writer marks a run with the registered clock, and SQLite, which has no server clock of its own, writes the
/// mark as it is given. A store that measured the age on the wall clock while the writer read a registered one (a fake clock
/// in a test host, a clock a consumer controls) closed every run that was beating as orphaned on the first scan, because the
/// mark and the age were read from two different times. The check goes through <c>UseSqlite()</c>, the registration a host
/// uses. PostgreSQL and SQL Server read the database server's clock for both and do not use the registered one.
/// </para>
/// </remarks>
/// <param name="schema">The table prefix shared by the tests of this class.</param>
public sealed class RunStoreClockRegistrationTests(SqliteSchemaFixture schema) : IClassFixture<SqliteSchemaFixture>
{
    private static readonly TimeSpan Threshold = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task A_heartbeat_marked_with_the_registered_clock_is_aged_by_that_clock()
    {
        await schema.ResetAsync();
        var clock = new ManualClock();
        await using var provider = Build(clock);
        var runs = provider.GetRequiredService<IRunStore>();
        var runId = TraconId.NewId();

        await runs.StartRunAsync(Start(runId, clock), TestContext.Current.CancellationToken);
        await runs.TouchHeartbeatAsync([runId], clock.GetUtcNow(), TestContext.Current.CancellationToken);

        // The mark is fresh on the clock it was written with; the wall clock is weeks past it.
        (await runs.ClaimOrphanedRunsAsync(Threshold, 10, TestContext.Current.CancellationToken))
            .ShouldBeEmpty("a heartbeat marked a moment ago on the registered clock is not old");

        clock.Advance(Threshold + TimeSpan.FromMinutes(1));

        var claimed = (await runs.ClaimOrphanedRunsAsync(Threshold, 10, TestContext.Current.CancellationToken)).ShouldHaveSingleItem();

        claimed.Id.ShouldBe(runId);
        claimed.CompletedAt.ShouldBe(clock.GetUtcNow(), "the run is closed at the registered clock's time, which is after its start");
    }

    [Fact]
    public async Task A_run_that_never_beat_is_aged_from_its_start_on_the_registered_clock()
    {
        await schema.ResetAsync();
        var clock = new ManualClock();
        await using var provider = Build(clock);
        var runs = provider.GetRequiredService<IRunStore>();
        var runId = TraconId.NewId();

        await runs.StartRunAsync(Start(runId, clock), TestContext.Current.CancellationToken);

        (await runs.ClaimOrphanedRunsAsync(Threshold, 10, TestContext.Current.CancellationToken))
            .ShouldBeEmpty("a run that started a moment ago on the registered clock is not old");

        clock.Advance(Threshold + TimeSpan.FromMinutes(1));

        (await runs.ClaimOrphanedRunsAsync(Threshold, 10, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem().Id.ShouldBe(runId);
    }

    /// <summary>
    /// A threshold of zero or less would count a run that was marked a moment ago as orphaned and close it while it is
    /// beating. The store refuses it, and the run stays open.
    /// </summary>
    /// <param name="staleAfterSeconds">The threshold, in seconds.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public async Task A_threshold_of_zero_or_less_is_refused_and_closes_no_run(int staleAfterSeconds)
    {
        await schema.ResetAsync();
        var clock = new ManualClock();
        await using var provider = Build(clock);
        var runs = provider.GetRequiredService<IRunStore>();
        var runId = TraconId.NewId();

        await runs.StartRunAsync(Start(runId, clock), TestContext.Current.CancellationToken);
        await runs.TouchHeartbeatAsync([runId], clock.GetUtcNow(), TestContext.Current.CancellationToken);

        clock.Advance(TimeSpan.FromMilliseconds(1));

        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            async () => await runs.ClaimOrphanedRunsAsync(TimeSpan.FromSeconds(staleAfterSeconds), 10, TestContext.Current.CancellationToken));

        (await runs.GetRunAsync(runId, TestContext.Current.CancellationToken))!.Status.ShouldBe(RunStatus.Running);
    }

    private static RunStartInfo Start(Guid runId, TimeProvider clock)
        => new() { RunId = runId, AgentName = "clock-agent", StartedAt = clock.GetUtcNow() };

    private ServiceProvider Build(TimeProvider clock)
    {
        var services = new ServiceCollection();
        services.AddSingleton(clock);
        services.AddTracon().UseSqlite(options =>
        {
            options.ConnectionString = schema.Context.Options.ConnectionString;
            options.TablePrefix = schema.Context.TablePrefix;
            options.AutoApplyMigrations = false;
        });

        return services.BuildServiceProvider();
    }
}
