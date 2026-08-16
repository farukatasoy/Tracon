using Npgsql;
using Testcontainers.PostgreSql;

namespace AgentPrism.PostgreSql.IntegrationTests.Infrastructure;

/// <summary>
/// The disposable PostgreSQL container shared by all integration tests.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No test connects to a remote or shared server.</strong> Testcontainers
/// starts a local container on every run and tears it down afterward.
/// </para>
/// <para>
/// The container starts once for the whole assembly; the schema is shared per
/// contract test CLASS (see <see cref="PostgresSchemaFixture"/>,
/// <see cref="PostgresTestContext"/>), and isolation between tests is achieved
/// by resetting data (K-390). The migration lock is also now scoped to the
/// schema rather than the whole database (K-389); together these two let class
/// fixtures run their migrations in PARALLEL.
/// </para>
/// <para>
/// 🚨 The image is <c>pgvector/pgvector:pg18</c>, NOT <c>postgres:18-alpine</c>
/// (Phase 51). Migration 0024 runs <c>CREATE EXTENSION IF NOT EXISTS vector;</c>,
/// and it applies on EVERY test (not just vector tests); since the plain
/// Postgres image does not carry the extension, the migration set would blow up
/// across the whole test suite. The <c>pgvector/pgvector</c> image adds only
/// this extension on top of the official <c>postgres</c> image and creates no
/// other behavioral difference.
/// </para>
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg18")
        .WithDatabase("agentprism_tests")
        .WithCleanUp(true)
        .Build();

    /// <summary>Gets the connection string of the running container.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <inheritdoc />
    /// <remarks>
    /// 🚨 The <c>vector</c> extension is created here right after the container
    /// starts, BEFORE any schema class fixture migrates. Migration 0024's own
    /// <c>CREATE EXTENSION IF NOT EXISTS vector;</c> statement is also
    /// idempotent and correct on its own, but the <c>pg_extension</c> catalog is
    /// shared DATABASE-WIDE — when dozens of schema class fixtures (see
    /// <see cref="PostgresSchemaFixture"/>) run their first migration
    /// concurrently, all of them try to create the same row and hit a
    /// uniqueness violation (SQLSTATE 23505). <c>MigrationRunner.ApplyOneAsync</c>
    /// retries that violation (K-389), but the realistic production scenario
    /// (installing the extension on a database ONCE, at setup time) is mirrored
    /// here, which avoids the race entirely — a race produced only by the
    /// test's artificial "29 schemas migrate for the first time at once"
    /// conditions, one that would not occur in a real deployment.
    /// </remarks>
    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE EXTENSION IF NOT EXISTS vector;";
        await command.ExecuteNonQueryAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
