using System.Globalization;
using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// How the SQL Server lease stores write a lease's end on the server's clock, and how they read a lease an older version
/// of the code wrote with the node's own clock.
/// </summary>
/// <remarks>The scenarios live in <see cref="SqlLeaseTimeContract"/>, which the PostgreSQL project runs too.</remarks>
/// <param name="schema">The schema shared by the tests of this class.</param>
public sealed class SqlLeaseTimeTests(SqlServerSchemaFixture schema)
    : SqlLeaseTimeContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<(ISingletonLeaseStore Leases, IJobStore Jobs)> OpenAsync()
    {
        await schema.ResetAsync();

        return (schema.Context.SingletonLeases, schema.Context.Jobs);
    }

    /// <inheritdoc />
    protected override async ValueTask<long> JobLeaseSpanMicrosecondsAsync(Guid jobId)
        => await schema.Context.ScalarAsync<long>(
            $"SELECT DATEDIFF_BIG(MICROSECOND, started_at, lease_until) FROM {schema.Context.SchemaName}.jobs WHERE id = '{jobId}'");

    /// <inheritdoc />
    protected override async ValueTask<long> SingletonLeaseSpanMicrosecondsAsync(string name)
        => await schema.Context.ScalarAsync<long>(
            $"SELECT DATEDIFF_BIG(MICROSECOND, updated_at, expires_at) FROM {schema.Context.SchemaName}.singleton_leases WHERE name = '{name}'");

    /// <inheritdoc />
    protected override async ValueTask WriteOlderNodeJobLeaseAsync(Guid jobId, string owner, DateTimeOffset leaseUntil)
        => await schema.Context.ExecuteAsync($"""
            UPDATE {schema.Context.SchemaName}.jobs
               SET status = 2, lease_owner = '{owner}', lease_until = CAST('{Literal(leaseUntil)}' AS datetimeoffset(7)), attempt = 1, started_at = CAST('{Literal(leaseUntil)}' AS datetimeoffset(7))
             WHERE id = '{jobId}';
            """);

    /// <inheritdoc />
    protected override async ValueTask WriteOlderNodeSingletonLeaseAsync(string name, string owner, DateTimeOffset expiresAt)
        => await schema.Context.ExecuteAsync($"""
            INSERT INTO {schema.Context.SchemaName}.singleton_leases (name, owner_id, expires_at, updated_at)
            VALUES ('{name}', '{owner}', CAST('{Literal(expiresAt)}' AS datetimeoffset(7)), CAST('{Literal(expiresAt)}' AS datetimeoffset(7)));
            """);

    private static string Literal(DateTimeOffset instant) => instant.ToString("O", CultureInfo.InvariantCulture);
}
