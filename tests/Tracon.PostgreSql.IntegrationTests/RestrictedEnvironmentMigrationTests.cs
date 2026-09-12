using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// Migration behavior on a PostgreSQL server that does NOT carry the
/// <c>pgvector</c> extension — a managed PostgreSQL instance without
/// permission (or ability) to install it (phase 67, DoD bullets 1-2).
/// </summary>
/// <remarks>
/// Deliberately does NOT use the shared <see cref="Infrastructure.PostgresFixture"/>
/// assembly fixture: that one runs the <c>pgvector/pgvector</c> image (K-346),
/// which defeats the point of this test. Runs its own plain <c>postgres</c>
/// container instead — acceptable here since there are only two tests, each
/// building its <see cref="MigrationRunner"/> directly (the same pattern as
/// <c>MigrationDiagnosticsTests.Unreachable_provider_returns_CanConnect_false</c>),
/// not through <see cref="Infrastructure.PostgresTestContext"/> (which is
/// wired to the shared fixture).
/// </remarks>
public sealed class RestrictedEnvironmentMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("tracon_restricted")
        .WithCleanUp(true)
        .Build();

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public ValueTask DisposeAsync() => new(_container.DisposeAsync().AsTask());

    /// <summary>DoD bullet 1: the core set completes on a server that never had <c>pgvector</c> in the first place.</summary>
    [Fact]
    public async Task Core_set_completes_without_pgvector_on_the_server()
    {
        var schemaName = Infrastructure.PostgresTestContext.NewSchemaName();
        await using var dataSource = new NpgsqlDataSourceBuilder(_container.GetConnectionString()).Build();

        var runner = new MigrationRunner(
            new SqlStoreContext
            {
                DataSource = dataSource,
                Dialect = new PostgresDialect(schemaName),
                CommandTimeoutSeconds = 30,
                ProviderName = "PostgreSQL",
                EnabledMigrationSets = System.Collections.Immutable.ImmutableHashSet<string>.Empty,
            },
            NullLogger<MigrationRunner>.Instance);

        var coreCount = MigrationDescriptor
            .Discover(typeof(MigrationRunner).Assembly, "Tracon.PostgreSql.Migrations.")
            .Count;

        (await runner.ApplyAsync()).ShouldBe(coreCount);

        await using var command = dataSource.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'vector');");
        var hasVectorExtension = (bool)(await command.ExecuteScalarAsync())!;

        hasVectorExtension.ShouldBeFalse();
    }

    /// <summary>
    /// DoD bullet 2: <c>EnableKnowledge = true</c> against the same server
    /// fails with a clear, readable error — not a raw <see cref="NpgsqlException"/>
    /// leaking past <see cref="MigrationRunner"/>'s wrapping.
    /// </summary>
    [Fact]
    public async Task Enabling_knowledge_without_pgvector_fails_with_a_readable_error()
    {
        var schemaName = Infrastructure.PostgresTestContext.NewSchemaName();
        await using var dataSource = new NpgsqlDataSourceBuilder(_container.GetConnectionString()).Build();

        var runner = new MigrationRunner(
            new SqlStoreContext
            {
                DataSource = dataSource,
                Dialect = new PostgresDialect(schemaName),
                CommandTimeoutSeconds = 30,
                ProviderName = "PostgreSQL",
                EnabledMigrationSets = new HashSet<string>(StringComparer.Ordinal) { "knowledge" },
            },
            NullLogger<MigrationRunner>.Instance);

        var exception = await Should.ThrowAsync<TraconException>(async () => await runner.ApplyAsync());

        exception.Message.ShouldContain("0001_vector");
        exception.Message.ShouldContain("vector");
        exception.InnerException.ShouldBeOfType<PostgresException>();
    }
}
