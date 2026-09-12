using Testcontainers.MsSql;

namespace Tracon.SqlServer.IntegrationTests.Infrastructure;

/// <summary>
/// The disposable SQL Server container shared by all integration tests.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No test connects to a remote or shared server.</strong>
/// Testcontainers starts a local container on every run and tears it down
/// afterward.
/// </para>
/// <para>
/// The container starts once for the whole assembly; the schema is shared per
/// contract test CLASS (see <see cref="SqlServerSchemaFixture"/>,
/// <see cref="SqlServerTestContext"/>), and isolation between tests is
/// achieved by resetting data (K-390). The migration lock is also now scoped
/// to the schema rather than the whole database (K-389); together these two
/// let the 29 class fixtures run their migrations in PARALLEL.
/// </para>
/// <para>
/// 🚨 The SQL Server container needs ~2 GB of memory; it is noticeably heavier
/// than the PostgreSQL image. Check the resource limit in CI job definitions.
/// </para>
/// </remarks>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .WithCleanUp(true)
            .Build();

    /// <summary>Gets the connection string of the running container.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => await _container.StartAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
