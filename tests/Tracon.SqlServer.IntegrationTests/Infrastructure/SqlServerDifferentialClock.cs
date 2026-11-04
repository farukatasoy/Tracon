using System.Globalization;
using Tracon.Tests.Common;
using Tracon.Tests.Differential;

namespace Tracon.SqlServer.IntegrationTests.Infrastructure;

/// <summary>
/// The clock the differential job sequences move in place of the SQL Server's: a one-row table and a function that reads
/// it, which the lease statements are pointed at.
/// </summary>
/// <remarks>
/// The lease statements read the server's UTC clock, which a test cannot move. <see cref="SqlLeaseClockStandIn"/> points
/// them at <c>differential_now()</c> in the test's schema and this class moves the row it reads. Real time is covered where
/// it is the clock: the store contracts and the clock-skew contract.
/// </remarks>
internal sealed class SqlServerDifferentialClock : DifferentialClock
{
    private const string ServerClock = "TODATETIMEOFFSET(SYSUTCDATETIME(), 0)";

    private static readonly DateTimeOffset Start = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly SqlServerTestContext _context;
    private DateTimeOffset _now = Start;

    private SqlServerDifferentialClock(SqlServerTestContext context) => _context = context;

    /// <inheritdoc />
    public override DateTimeOffset Now => _now;

    /// <summary>Stands the clock in the schema, points the lease statements at it and sets it to its start.</summary>
    /// <param name="context">The test context whose lease statements are redirected.</param>
    /// <returns>The clock.</returns>
    public static async ValueTask<SqlServerDifferentialClock> CreateAsync(SqlServerTestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var schema = context.SchemaName;

        await context.ExecuteAsync($"""
            IF OBJECT_ID(N'{schema}.differential_clock', N'U') IS NULL
            CREATE TABLE {schema}.differential_clock (id int NOT NULL PRIMARY KEY, instant datetimeoffset(7) NOT NULL);
            """);

        // A function is created in a batch of its own.
        await context.ExecuteAsync($"""
            CREATE OR ALTER FUNCTION {schema}.differential_now() RETURNS datetimeoffset(7)
            AS BEGIN RETURN (SELECT instant FROM {schema}.differential_clock WHERE id = 1); END;
            """);

        SqlLeaseClockStandIn.Install(context.StoreContext.Sql, ServerClock, $"{schema}.differential_now()");

        var clock = new SqlServerDifferentialClock(context);
        await clock.WriteAsync();

        return clock;
    }

    /// <inheritdoc />
    public override async ValueTask AdvanceAsync(TimeSpan amount)
    {
        _now += amount;
        await WriteAsync();
    }

    private async ValueTask WriteAsync()
        => await _context.ExecuteAsync($"""
            DELETE FROM {_context.SchemaName}.differential_clock WHERE id = 1;
            INSERT INTO {_context.SchemaName}.differential_clock (id, instant)
            VALUES (1, CAST('{_now.ToString("O", CultureInfo.InvariantCulture)}' AS datetimeoffset(7)));
            """);
}
