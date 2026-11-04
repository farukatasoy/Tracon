using Tracon.PostgreSql.IntegrationTests.Infrastructure;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// What the PostgreSQL run store does with a beating run when the node that closes orphaned runs reads a clock that runs ahead.
/// </summary>
/// <remarks>
/// The scenarios live in <see cref="RunHeartbeatUnderClockSkewContract"/>, which the SQL Server project runs too.
/// </remarks>
/// <param name="schema">The schema shared by the tests of this class.</param>
public sealed class RunHeartbeatUnderClockSkewTests(PostgresSchemaFixture schema)
    : RunHeartbeatUnderClockSkewContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<(IRunStore Runs, ISingletonLeaseStore Leases, IJobStore Jobs)> OpenAsync()
    {
        await schema.ResetAsync();

        var context = schema.Context.StoreContext;

        return (schema.Context.Runs, new SqlSingletonLeaseStore(context, TimeProvider.System), new SqlJobStore(context, timeProvider: TimeProvider.System));
    }
}
