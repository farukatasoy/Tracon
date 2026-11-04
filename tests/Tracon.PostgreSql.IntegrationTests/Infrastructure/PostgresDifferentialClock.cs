using System.Globalization;
using Tracon.Tests.Common;
using Tracon.Tests.Differential;

namespace Tracon.PostgreSql.IntegrationTests.Infrastructure;

/// <summary>
/// The clock the differential job sequences move in place of the PostgreSQL server's: a one-row table and a function that
/// reads it, which the lease statements are pointed at.
/// </summary>
/// <remarks>
/// The lease statements read <c>statement_timestamp()</c>, which a test cannot move. <see cref="SqlLeaseClockStandIn"/>
/// points them at <c>differential_now()</c> in the test's schema and this class moves the row it reads. Real time is
/// covered where it is the clock: the store contracts and the clock-skew contract.
/// </remarks>
internal sealed class PostgresDifferentialClock : DifferentialClock
{
    private static readonly DateTimeOffset Start = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly PostgresTestContext _context;
    private DateTimeOffset _now = Start;

    private PostgresDifferentialClock(PostgresTestContext context) => _context = context;

    /// <inheritdoc />
    public override DateTimeOffset Now => _now;

    /// <summary>Stands the clock in the schema, points the lease statements at it and sets it to its start.</summary>
    /// <param name="context">The test context whose lease statements are redirected.</param>
    /// <returns>The clock.</returns>
    public static async ValueTask<PostgresDifferentialClock> CreateAsync(PostgresTestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var schema = context.SchemaName;

        await context.ExecuteAsync($"""
            CREATE TABLE IF NOT EXISTS {schema}.differential_clock (id integer PRIMARY KEY, instant timestamptz NOT NULL);

            CREATE OR REPLACE FUNCTION {schema}.differential_now() RETURNS timestamptz
                LANGUAGE sql STABLE AS $$ SELECT instant FROM {schema}.differential_clock WHERE id = 1 $$;
            """);

        SqlLeaseClockStandIn.Install(context.StoreContext.Sql, "statement_timestamp()", $"{schema}.differential_now()");

        var clock = new PostgresDifferentialClock(context);
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
            INSERT INTO {_context.SchemaName}.differential_clock (id, instant)
            VALUES (1, '{_now.ToString("O", CultureInfo.InvariantCulture)}'::timestamptz)
            ON CONFLICT (id) DO UPDATE SET instant = EXCLUDED.instant;
            """);
}
