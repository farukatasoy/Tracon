using Tracon.Sqlite.IntegrationTests.Infrastructure;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>What the SQLite lease stores do when the wall clock steps while a lease is held.</summary>
/// <remarks>The scenarios live in <see cref="LeaseWallStepContract"/>, which the in-memory stores run too.</remarks>
/// <param name="schema">The table prefix shared by the tests of this class.</param>
public sealed class SqliteLeaseWallStepTests(SqliteSchemaFixture schema)
    : LeaseWallStepContract, IClassFixture<SqliteSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<(IJobStore Jobs, ISingletonLeaseStore Leases)> OpenAsync(TimeProvider clock)
    {
        await schema.ResetAsync();

        var context = schema.Context.StoreContext;

        return (new SqlJobStore(context, timeProvider: clock), new SqlSingletonLeaseStore(context, clock));
    }
}
