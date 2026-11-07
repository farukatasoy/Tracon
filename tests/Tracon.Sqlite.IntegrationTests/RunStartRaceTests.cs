using Tracon.Sqlite.IntegrationTests.Infrastructure;
using Tracon.Tests.Common;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// A call that overlaps the second start of the same run neither fails nor loses the run.
/// </summary>
/// <remarks>
/// The same race runs against SQL Server, where it found deadlock victims while the clustered key of <c>runs</c> moved with
/// the start time. SQLite keys its table on a row id that no write changes; the test keeps that true.
/// </remarks>
public sealed class RunStartRaceTests(SqliteSchemaFixture schema) : IClassFixture<SqliteSchemaFixture>
{
    private const int Rounds = 600;

    private const int Appenders = 3;

    private const int Readers = 3;

    private const int FirstWriteRounds = 300;

    private const int FirstWriters = 6;

    [Fact]
    public async Task An_event_append_or_a_read_that_overlaps_a_start_of_the_same_run_neither_fails_nor_loses_the_run()
    {
        await schema.ResetAsync();
        schema.Tenant.TenantId = "default";

        var failures = await RunStartRace.RunAsync(schema.Context.Runs, Rounds, Appenders, Readers);

        failures.ShouldBeEmpty("a call failed or lost the row while the second start ran");
    }

    [Fact]
    public async Task Writers_that_start_one_new_run_at_the_same_time_all_succeed_and_leave_the_run()
    {
        await schema.ResetAsync();
        schema.Tenant.TenantId = "default";

        var failures = await RunStartRace.RunFirstWriteAsync(schema.Context.Runs, FirstWriteRounds, FirstWriters);

        failures.ShouldBeEmpty("a writer failed, or the run was lost, while the writers started the same new run");
    }
}
