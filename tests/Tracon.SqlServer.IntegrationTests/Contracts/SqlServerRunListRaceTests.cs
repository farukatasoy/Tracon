using Tracon.SqlServer.IntegrationTests.Infrastructure;
using Tracon.Tests.Common;

namespace Tracon.SqlServer.IntegrationTests.Contracts;

/// <summary>
/// A listing that is read while a run moves in the index it is read from never returns a run twice and loses no run that stands still.
/// </summary>
/// <remarks>
/// <para>
/// The clustered index of <c>runs</c> is keyed on <c>started_at</c>, so the second start of a queued run moves its row from
/// the back of the list to the front. The list is read at <c>READ COMMITTED</c> and takes only short shared locks, so a scan
/// that is running when a row moves could see it at both places or at neither. A scan of the raw index showed exactly that;
/// these cases read the list the way the store does, through <see cref="IRunStore.QueryRunsAsync"/>, with the move released
/// at the same moment as the listings (<see cref="RunStartRace.RunListingAsync"/>).
/// </para>
/// <para>
/// Measured: the listing returned no run twice and lost no run that stood still, but it left the moved run out in almost every
/// listing that was released together with the move (885 of 900). That one is the transient of a read that holds no snapshot,
/// and the case does not hold the moved run to it: it is there in the next listing.
/// </para>
/// </remarks>
public sealed class SqlServerRunListRaceTests(SqlServerSchemaFixture schema) : IClassFixture<SqlServerSchemaFixture>
{
    private const int Rounds = 300;

    private const int Bystanders = 49;

    private const int Listers = 3;

    [Fact]
    public async Task A_listing_that_overlaps_the_move_of_a_run_returns_no_run_twice_and_loses_no_run_that_stood_still()
    {
        await schema.ResetAsync();
        schema.Tenant.TenantId = "default";

        var failures = await RunStartRace.RunListingAsync(schema.Context.Runs, Rounds, Bystanders, Listers);

        failures.ShouldBeEmpty("a listing returned a run twice, or left out a run that stood still, while another run moved");
    }
}
