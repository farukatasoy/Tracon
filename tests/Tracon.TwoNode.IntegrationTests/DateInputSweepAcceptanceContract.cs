using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Tests.Common;
using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>
/// No date or time a caller sends makes the server fail, on a real database server.
/// </summary>
/// <remarks>
/// <para>
/// The sweep is <see cref="DateInputSweep"/>: every date input of the HTTP surface, driven with the instants a server's own
/// date type breaks on. The in-memory store and SQLite run it in the functional tests; here it runs where the date
/// types have edges of their own: SQL Server's legacy <c>datetime</c> starts in 1753 and its <c>datetime2</c> stops
/// at the end of 9999, and PostgreSQL reads the first and the last instant of <see cref="DateTimeOffset"/> as its
/// infinities, so a series generated up to one of them never ends.
/// </para>
/// </remarks>
public abstract class DateInputSweepAcceptanceContract
{
    /// <summary>Gets a schema no other test uses, on the class's server.</summary>
    /// <returns>The database the host connects to.</returns>
    protected abstract TwoNodeDatabase NewDatabase();

    /// <summary>Every date input answers without a server failure, with rows at both ends of the calendar in the tables.</summary>
    /// <returns>A task that completes when the sweep is done.</returns>
    [Fact]
    public async Task No_date_input_makes_the_server_fail_with_rows_at_both_ends_of_the_calendar()
    {
        await using var host = await TraconTestHost.StartAsync(configureTracon: NewDatabase().Use);
        await DateInputSweep.SeedEdgeRowsAsync(host.Services);

        (await DateInputSweep.FindServerErrorsAsync(host.Client)).ShouldBeEmpty();
    }
}
