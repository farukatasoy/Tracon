using System.Globalization;
using Tracon.PostgreSql.IntegrationTests.Infrastructure;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// How the PostgreSQL lease stores write a lease's end on the server's clock, and how they read a lease an older version
/// of the code wrote with the node's own clock.
/// </summary>
/// <remarks>The scenarios live in <see cref="SqlLeaseTimeContract"/>, which the SQL Server project runs too.</remarks>
/// <param name="schema">The schema shared by the tests of this class.</param>
public sealed class SqlLeaseTimeTests(PostgresSchemaFixture schema)
    : SqlLeaseTimeContract, IClassFixture<PostgresSchemaFixture>
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
            $"SELECT (extract(epoch FROM (lease_until - started_at)) * 1000000)::bigint FROM {schema.Context.SchemaName}.jobs WHERE id = '{jobId}'");

    /// <inheritdoc />
    protected override async ValueTask<long> SingletonLeaseSpanMicrosecondsAsync(string name)
        => await schema.Context.ScalarAsync<long>(
            $"SELECT (extract(epoch FROM (expires_at - updated_at)) * 1000000)::bigint FROM {schema.Context.SchemaName}.singleton_leases WHERE name = '{name}'");

    /// <inheritdoc />
    protected override async ValueTask WriteOlderNodeJobLeaseAsync(Guid jobId, string owner, DateTimeOffset leaseUntil)
        => await schema.Context.ExecuteAsync($"""
            UPDATE {schema.Context.SchemaName}.jobs
               SET status = 2, lease_owner = '{owner}', lease_until = '{Literal(leaseUntil)}'::timestamptz, attempt = 1, started_at = '{Literal(leaseUntil)}'::timestamptz
             WHERE id = '{jobId}';
            """);

    /// <inheritdoc />
    protected override async ValueTask WriteOlderNodeSingletonLeaseAsync(string name, string owner, DateTimeOffset expiresAt)
        => await schema.Context.ExecuteAsync($"""
            INSERT INTO {schema.Context.SchemaName}.singleton_leases (name, owner_id, expires_at, updated_at)
            VALUES ('{name}', '{owner}', '{Literal(expiresAt)}'::timestamptz, '{Literal(expiresAt)}'::timestamptz);
            """);

    private static string Literal(DateTimeOffset instant) => instant.ToString("O", CultureInfo.InvariantCulture);
}
