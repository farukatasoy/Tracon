using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// What the SQL Server lease stores do when the two nodes that share a database disagree about the time.
/// </summary>
/// <remarks>
/// The scenarios live in <see cref="SqlLeaseUnderClockSkewContract"/>, which the PostgreSQL project runs too. Both
/// nodes here read a manual clock and the database server's own clock is never consulted.
/// </remarks>
/// <param name="schema">The schema shared by the tests of this class.</param>
public sealed class SqlLeaseUnderClockSkewTests(SqlServerSchemaFixture schema)
    : SqlLeaseUnderClockSkewContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<SkewedStores> OpenAsync(TimeProvider firstClock, TimeProvider secondClock)
    {
        await schema.ResetAsync();

        var context = schema.Context.StoreContext;

        return new SkewedStores(
            new SqlSingletonLeaseStore(context, firstClock),
            new SqlSingletonLeaseStore(context, secondClock),
            new SqlJobStore(context, timeProvider: firstClock),
            new SqlJobStore(context, timeProvider: secondClock));
    }
}
