using System.Globalization;
using Microsoft.Data.SqlClient;
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

    /// <summary>The image a run uses when <see cref="ImageVariable"/> is empty or unset.</summary>
    public const string DefaultImage = "mcr.microsoft.com/mssql/server:2022-latest";

    /// <summary>Gets the image this run starts: the variable when it has a value, the default image otherwise.</summary>
    public static string Image => Environment.GetEnvironmentVariable(ImageVariable) is { } image && !string.IsNullOrWhiteSpace(image)
        ? image
        : DefaultImage;

    /// <summary>The environment variable that sets the server collation (<c>MSSQL_COLLATION</c>).</summary>
    public const string CollationVariable = "TRACON_TEST_SQLSERVER_COLLATION";

    /// <summary>The environment variable that sets the server language (<c>MSSQL_LCID</c>), for example 1055 for Turkish.</summary>
    public const string LcidVariable = "TRACON_TEST_SQLSERVER_LCID";

    private static readonly TimeSpan CollationChangeBudget = TimeSpan.FromMinutes(6);

    private static readonly TimeSpan CollationProbeInterval = TimeSpan.FromSeconds(5);

    private readonly MsSqlContainer _container = CreateContainer();

    /// <summary>Gets the connection string of the running container.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        var collation = Environment.GetEnvironmentVariable(CollationVariable);

        if (!string.IsNullOrWhiteSpace(collation))
        {
            await WaitForCollationAsync(collation);
        }
    }

    /// <summary>
    /// The image applies <c>MSSQL_COLLATION</c> after the server first accepts connections: it starts with its default
    /// collation, passes the readiness probe, then rebuilds the system databases for the collation asked for and drops every
    /// connection while it does. Returning at the probe raced that rebuild (see <c>ServerCollationTests</c>). The wait ends
    /// when two probes, a few seconds apart, find the collation asked for on the same server process.
    /// </summary>
    private async Task WaitForCollationAsync(string expected)
    {
        var builder = new SqlConnectionStringBuilder(ConnectionString) { Pooling = false };
        var deadline = DateTime.UtcNow + CollationChangeBudget;
        string? previousStart = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await using var connection = new SqlConnection(builder.ConnectionString);
                await connection.OpenAsync();

                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT CONVERT(nvarchar(128), SERVERPROPERTY('Collation')), CONVERT(nvarchar(64), sqlserver_start_time, 126) FROM sys.dm_os_sys_info;";

                await using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync()
                    && string.Equals(reader.GetString(0), expected, StringComparison.OrdinalIgnoreCase))
                {
                    var start = reader.GetString(1);

                    if (string.Equals(start, previousStart, StringComparison.Ordinal))
                    {
                        SqlConnection.ClearAllPools();
                        return;
                    }

                    previousStart = start;
                }
                else
                {
                    previousStart = null;
                }
            }
            catch (SqlException)
            {
                // The server drops connections while it rebuilds; the next probe finds out whether it is done.
                previousStart = null;
            }

            await Task.Delay(CollationProbeInterval); // delay: fixture
        }

        throw new TimeoutException(string.Create(CultureInfo.InvariantCulture, $"The server did not report the collation '{expected}' within {CollationChangeBudget}."));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>
    /// One run covers one cell of the version and collation matrix. A variable
    /// that is empty or unset keeps the default, so a plain run stays what CI runs.
    /// </summary>
    private static MsSqlContainer CreateContainer()
    {
        var collation = Environment.GetEnvironmentVariable(CollationVariable);
        var lcid = Environment.GetEnvironmentVariable(LcidVariable);

        var builder = new MsSqlBuilder(Image)
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
