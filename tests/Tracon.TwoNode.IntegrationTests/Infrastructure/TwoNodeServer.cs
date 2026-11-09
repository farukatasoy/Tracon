using System.Globalization;
using Microsoft.Data.SqlClient;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Tracon.Tests.Common;

namespace Tracon.TwoNode.IntegrationTests.Infrastructure;

/// <summary>
/// A database server in a container that two Tracon hosts share, and the schema each test gets on it.
/// </summary>
/// <remarks>
/// <para>
/// One container per provider for the whole run: the collection fixture of <see cref="TwoNodeAcceptanceSerial"/> starts
/// it when the first class of the collection runs and removes it when the last one is done.
/// <strong>No test connects to a remote or shared server.</strong> Every test takes a schema of its own
/// (<see cref="NewDatabase"/>): the scenarios write runs, jobs, counters and OAuth servers, and a row one test leaves
/// behind must not be read by another.
/// </para>
/// <para>
/// A host migrates its schema when it starts, as a production host does. The second host finds it migrated.
/// </para>
/// </remarks>
public abstract class TwoNodeServer : IAsyncLifetime
{
    /// <summary>Gets the provider's name, for messages.</summary>
    public abstract string Name { get; }

    /// <summary>Gets a schema no other test uses, on this server.</summary>
    /// <returns>The database the two hosts of one test connect to.</returns>
    public abstract TwoNodeDatabase NewDatabase();

    /// <summary>
    /// Gets a schema no other test uses, owned by a login of its own with the given password, so the connection string
    /// a host reads carries a value nobody could guess.
    /// </summary>
    /// <param name="password">The login's password.</param>
    /// <returns>The database the hosts of one test connect to, through that login.</returns>
    public abstract Task<TwoNodeDatabase> NewDatabaseForLoginAsync(string password);

    /// <inheritdoc />
    public abstract ValueTask InitializeAsync();

    /// <inheritdoc />
    public abstract ValueTask DisposeAsync();

    /// <summary>Generates a schema name no other test uses: lowercase letters and digits, and short.</summary>
    /// <returns>The schema name.</returns>
    protected static string NewSchemaName()
        => "t_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..16];
}

/// <summary>
/// The PostgreSQL container. The image is the one the provider integration project uses, with the extension its
/// "knowledge" migration set needs created before any schema migrates.
/// </summary>
public sealed class PostgreSqlTwoNodeServer : TwoNodeServer
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg18")
        .WithDatabase("tracon_two_node")
        .WithCleanUp(true)
        .Build();

    /// <inheritdoc />
    public override string Name => "PostgreSQL";

    /// <inheritdoc />
    public override TwoNodeDatabase NewDatabase()
        => new PostgreSqlTwoNodeDatabase(_container.GetConnectionString(), NewSchemaName());

    /// <inheritdoc />
    public override async Task<TwoNodeDatabase> NewDatabaseForLoginAsync(string password)
    {
        var login = "canary_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..10];

        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE ROLE {login} LOGIN PASSWORD '{password}'; GRANT ALL ON DATABASE tracon_two_node TO {login};";
        await command.ExecuteNonQueryAsync();

        var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Username = login, Password = password }.ConnectionString;

        return new PostgreSqlTwoNodeDatabase(connectionString, NewSchemaName());
    }

    /// <inheritdoc />
    public override async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE EXTENSION IF NOT EXISTS vector;";
        await command.ExecuteNonQueryAsync();
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

/// <summary>The SQL Server container (about 2 GB of memory; the image is heavier than PostgreSQL's).</summary>
/// <remarks>
/// The hosts use a database of their own on the server, not <c>master</c>; it runs without the log flush on commit
/// (see <see cref="NonDurableSqlServerDatabase"/>).
/// </remarks>
public sealed class SqlServerTwoNodeServer : TwoNodeServer
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .WithCleanUp(true)
        .Build();

    /// <inheritdoc />
    public override string Name => "SQL Server";

    private string ConnectionString
    {
        get => field ?? throw new InvalidOperationException("The SQL Server container has not started.");
        set;
    }

    /// <inheritdoc />
    public override TwoNodeDatabase NewDatabase()
        => new SqlServerTwoNodeDatabase(ConnectionString, NewSchemaName());

    /// <inheritdoc />
    public override async Task<TwoNodeDatabase> NewDatabaseForLoginAsync(string password)
    {
        var login = "canary_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..10];

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE LOGIN [{login}] WITH PASSWORD = '{password}', CHECK_POLICY = OFF; ALTER SERVER ROLE sysadmin ADD MEMBER [{login}];";
        await command.ExecuteNonQueryAsync();

        var connectionString = new SqlConnectionStringBuilder(ConnectionString) { UserID = login, Password = password }.ConnectionString;

        return new SqlServerTwoNodeDatabase(connectionString, NewSchemaName());
    }

    /// <inheritdoc />
    public override async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        ConnectionString = await NonDurableSqlServerDatabase.CreateAsync(_container.GetConnectionString(), "tracon_two_node");

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DB_NAME(), delayed_durability_desc, recovery_model_desc FROM sys.databases WHERE database_id = DB_ID();";

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        if (!string.Equals(reader.GetString(0), "tracon_two_node", StringComparison.Ordinal)
            || !string.Equals(reader.GetString(1), "FORCED", StringComparison.Ordinal)
            || !string.Equals(reader.GetString(2), "SIMPLE", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The hosts' database is {reader.GetString(0)} with delayed durability {reader.GetString(1)} and recovery {reader.GetString(2)}; the options of NonDurableSqlServerDatabase did not take.");
        }
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
