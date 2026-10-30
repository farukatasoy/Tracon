using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// What actually happens when one of two worker processes dies mid-job, on SQL Server.
/// </summary>
/// <remarks>
/// <para>
/// The scenarios live in <see cref="TwoProcessLeaseTakeoverContract"/>, which the PostgreSQL project runs too. SQL
/// Server leases through its own query (<c>UPDLOCK</c>/<c>READPAST</c>, not <c>FOR UPDATE SKIP LOCKED</c>), so the
/// claim "a dead worker's job is not taken before its lease expires, and never runs twice at once" is measured here
/// and not assumed from PostgreSQL.
/// </para>
/// <para>🚨 A measurement, not a support statement.</para>
/// </remarks>
[Collection(TwoProcessFailureProof.Name)]
public sealed class TwoProcessLeaseTakeoverTests(SqlServerFixture fixture) : TwoProcessLeaseTakeoverContract
{
    /// <inheritdoc />
    protected override string Provider => WorkerHarnessContract.SqlServerProvider;

    /// <inheritdoc />
    protected override string ConnectionString => fixture.ConnectionString;

    /// <inheritdoc />
    protected override async ValueTask<MigratedSchema> MigrateNewSchemaAsync()
    {
        var context = SqlServerTestContext.Create(fixture, SqlServerTestContext.NewSchemaName());

        try
        {
            await context.Migrations.ApplyAsync();

            return new MigratedSchema(context.SchemaName, context.Jobs, context);
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }
}

/// <summary>
/// Serializes the process-level failure proofs against each other.
/// </summary>
/// <remarks>
/// Each test in this collection starts two real hosts and waits on a lease clock. Running them beside each other
/// would put four hosts and their connection pools on the same machine and stretch the very clock they measure.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class TwoProcessFailureProof
{
    public const string Name = "Two-process failure proof";
}
