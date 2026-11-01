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
/// achieved by resetting data. The migration lock is also now scoped
/// to the schema rather than the whole database; together these two
/// let the 29 class fixtures run their migrations in PARALLEL.
/// </para>
/// <para>
/// 🚨 The SQL Server container needs ~2 GB of memory; it is noticeably heavier
/// than the PostgreSQL image. Check the resource limit in CI job definitions.
/// </para>
/// </remarks>
public sealed class SqlServerFixture : IAsyncLifetime
{
    /// <summary>The environment variable that replaces the default image.</summary>
    public const string ImageVariable = "TRACON_TEST_SQLSERVER_IMAGE";

    /// <summary>The environment variable that sets the server collation (<c>MSSQL_COLLATION</c>).</summary>
    public const string CollationVariable = "TRACON_TEST_SQLSERVER_COLLATION";

    /// <summary>The environment variable that sets the server language (<c>MSSQL_LCID</c>), for example 1055 for Turkish.</summary>
    public const string LcidVariable = "TRACON_TEST_SQLSERVER_LCID";

    private readonly MsSqlContainer _container = CreateContainer();

    /// <summary>Gets the connection string of the running container.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => await _container.StartAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>
    /// One run covers one cell of the version and collation matrix. A variable
    /// that is empty or unset keeps the default, so a plain run stays what CI runs.
    /// </summary>
    private static MsSqlContainer CreateContainer()
    {
        var image = Environment.GetEnvironmentVariable(ImageVariable);
        var collation = Environment.GetEnvironmentVariable(CollationVariable);
        var lcid = Environment.GetEnvironmentVariable(LcidVariable);

        var builder = new MsSqlBuilder(string.IsNullOrWhiteSpace(image) ? "mcr.microsoft.com/mssql/server:2022-latest" : image)
            .WithCleanUp(true);

        if (!string.IsNullOrWhiteSpace(collation))
        {
            builder = builder.WithEnvironment("MSSQL_COLLATION", collation);
        }

        if (!string.IsNullOrWhiteSpace(lcid))
        {
            builder = builder.WithEnvironment("MSSQL_LCID", lcid);
        }

        return builder.Build();
    }
}
