using Npgsql;
using Testcontainers.PostgreSql;

namespace Tracon.PostgreSql.IntegrationTests.Infrastructure;

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
/// by resetting data. The migration lock is also now scoped to the
/// schema rather than the whole database; together these two let class
/// fixtures run their migrations in PARALLEL.
/// </para>
/// <para>
/// 🚨 The image is <c>pgvector/pgvector:pg18</c>, NOT <c>postgres:18-alpine</c>
/// The "knowledge" set's <c>0001_vector</c> migration runs
/// <c>CREATE EXTENSION IF NOT EXISTS vector;</c>; <see cref="PostgresTestContext"/>
/// enables that set by DEFAULT, so it applies on EVERY test (not
/// just vector tests) and the plain Postgres image would blow up the whole
/// test suite for lacking the extension. The <c>pgvector/pgvector</c> image
/// adds only this extension on top of the official <c>postgres</c> image and
/// creates no other behavioral difference.
/// </para>
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>The environment variable that replaces the default image.</summary>
    public const string ImageVariable = "TRACON_TEST_POSTGRES_IMAGE";

    /// <summary>
    /// The environment variable that initializes the cluster with an ICU locale
    /// (<c>--locale-provider=icu --icu-locale=…</c>), for example <c>tr-TR</c>.
    /// </summary>
    public const string IcuLocaleVariable = "TRACON_TEST_POSTGRES_ICU_LOCALE";

    private readonly PostgreSqlContainer _container = CreateContainer();

    /// <summary>Gets the connection string of the running container.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <inheritdoc />
    /// <remarks>
    /// 🚨 The <c>vector</c> extension is created here right after the container
    /// starts, BEFORE any schema class fixture migrates. The <c>0001_vector</c>
    /// migration's own <c>CREATE EXTENSION IF NOT EXISTS vector;</c> statement is
    /// also idempotent and correct on its own, but the <c>pg_extension</c> catalog is
    /// shared DATABASE-WIDE — when dozens of schema class fixtures (see
    /// <see cref="PostgresSchemaFixture"/>) run their first migration
    /// concurrently, all of them try to create the same row and hit a
    /// uniqueness violation (SQLSTATE 23505). <c>MigrationRunner.ApplyOneAsync</c>
    /// retries that violation, but the realistic production scenario
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

    /// <summary>
    /// One run covers one cell of the version and locale matrix. A variable that
    /// is empty or unset keeps the default, so a plain run stays what CI runs.
    /// The image ships only a few libc locales, so a Turkish locale goes through ICU.
    /// </summary>
    private static PostgreSqlContainer CreateContainer()
    {
        var image = Environment.GetEnvironmentVariable(ImageVariable);
        var icuLocale = Environment.GetEnvironmentVariable(IcuLocaleVariable);

        var builder = new PostgreSqlBuilder(string.IsNullOrWhiteSpace(image) ? "pgvector/pgvector:pg18" : image)
            .WithDatabase("tracon_tests")
            // The tests of the project share this server and run in parallel, each host with a pool of its own: the
            // default limit of 100 connections ran out ("too many clients already") in a full run.
            .WithCommand("-c", "max_connections=400")
            .WithCleanUp(true);

        if (!string.IsNullOrWhiteSpace(icuLocale))
        {
            builder = builder.WithEnvironment(
                "POSTGRES_INITDB_ARGS",
                $"--locale-provider=icu --icu-locale={icuLocale}");
        }

        return builder.Build();
    }
}
