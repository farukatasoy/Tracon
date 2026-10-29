using Tracon.PostgreSql.IntegrationTests.Infrastructure;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// What actually happens when one of two worker processes dies mid-job, on PostgreSQL (Phase 157).
/// </summary>
/// <remarks>
/// The scenarios live in <see cref="TwoProcessLeaseTakeoverContract"/>, which the SQL Server project runs too
/// (Phase 265). 🚨 A measurement, not a support statement.
/// </remarks>
[Collection(TwoProcessFailureProof.Name)]
public sealed class TwoProcessLeaseTakeoverTests(PostgresFixture fixture) : TwoProcessLeaseTakeoverContract
{
    /// <inheritdoc />
    protected override string Provider => WorkerHarnessContract.PostgreSqlProvider;

    /// <inheritdoc />
    protected override string ConnectionString => fixture.ConnectionString;

    /// <inheritdoc />
    protected override async ValueTask<MigratedSchema> MigrateNewSchemaAsync()
    {
        var context = PostgresTestContext.Create(fixture, PostgresTestContext.NewSchemaName());

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
/// Each test in this collection starts two real hosts and waits on a lease
/// clock. Running them beside each other would put four hosts and their
/// connection pools on the same machine and stretch the very clock they
/// measure - the "isolated green, full run red" class this repository has hit
/// before (docs/hafiza/test-yalitimi.md).
/// </remarks>
[CollectionDefinition(Name)]
public sealed class TwoProcessFailureProof
{
    public const string Name = "Two-process failure proof";
}
