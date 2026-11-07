using Tracon.SqlServer.IntegrationTests.Infrastructure;
using Tracon.Tests.Common;

namespace Tracon.SqlServer.IntegrationTests.Contracts;

/// <summary>
/// A call that overlaps the start of a run is not a deadlock victim, and the run does not vanish from it.
/// </summary>
/// <remarks>
/// <para>
/// A queued run is written twice: the placeholder, then the worker that runs it, which also sets the time the run started.
/// The clustered index of <c>runs</c> is keyed on <c>started_at</c>, so that second write moves the row, and it rewrites the
/// entry of <c>runs_pk</c> too, whose row locator is the clustered key. An event append (the check of its foreign key) and a
/// read of the run take the shared lock on <c>runs_pk</c> first and the clustered row second. A write that took the clustered
/// row first (<c>UPDLOCK</c>) and <c>runs_pk</c> second closed a cycle with them, and the server picked a victim. The write now
/// takes the <c>runs_pk</c> entry first (<c>XLOCK</c>), the order the others use, so the cycle cannot form. The row still
/// moves; only the order of the locks changed.
/// </para>
/// <para>
/// The race is not deterministic, so the tests run many rounds, each one start against several appends and reads released at
/// the same moment (<see cref="RunStartRace"/>). The store sends a statement it lost to a deadlock once more, so a victim leaves
/// no mark on the result of a call and the tests count them where the client reports them (<see cref="DeadlockVictimRecorder"/>):
/// with <c>UPDLOCK</c>, 600 rounds gave 70 victims and 3,000 rounds of a wider race 191, none of them visible in the results.
/// The deterministic cases of the retry are in <see cref="StatementDeadlockRetryTests"/>.
/// </para>
/// <para>
/// The read that finds no run is a separate matter and has nothing to do with the order of the locks: a read at
/// <c>READ UNCOMMITTED</c> takes none, and a row that moves under it is missed (<see cref="CatalogReadIsolationTests"/> keeps
/// such a level off the pooled connections that these reads draw).
/// </para>
/// </remarks>
public sealed class SqlServerRunDeadlockFindingTests(SqlServerSchemaFixture schema) : IClassFixture<SqlServerSchemaFixture>
{
    private const int Rounds = 600;

    private const int Appenders = 3;

    private const int Readers = 3;

    private const int FirstWriteRounds = 300;

    private const int FirstWriters = 6;

    [Fact]
    public async Task An_event_append_or_a_read_that_overlaps_a_start_of_the_same_run_is_not_a_deadlock_victim()
    {
        await schema.ResetAsync();
        schema.Tenant.TenantId = "default";
        using var victims = DeadlockVictimRecorder.Start(schema.Context.SchemaName);

        var failures = await RunStartRace.RunAsync(schema.Context.Runs, Rounds, Appenders, Readers);

        failures.ShouldBeEmpty("a call failed or lost the row while the second start ran");

        // A victim leaves no mark on the results above, so the count is read where the client reports it.
        victims.Victims.ShouldBeEmpty("a command was chosen as a deadlock victim and only the retry hid it");
    }

    [Fact]
    public async Task Writers_that_start_one_new_run_at_the_same_time_all_succeed_and_none_is_a_deadlock_victim()
    {
        await schema.ResetAsync();
        schema.Tenant.TenantId = "default";
        using var victims = DeadlockVictimRecorder.Start(schema.Context.SchemaName);

        var failures = await RunStartRace.RunFirstWriteAsync(schema.Context.Runs, FirstWriteRounds, FirstWriters);

        failures.ShouldBeEmpty("a writer failed, or the run was lost, while the writers started the same new run");
        victims.Victims.ShouldBeEmpty("a command was chosen as a deadlock victim and only the retry hid it");
    }
}
