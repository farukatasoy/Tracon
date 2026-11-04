using System.Globalization;
using Microsoft.Data.SqlClient;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace Tracon.TwoNode.IntegrationTests.Infrastructure;

/// <summary>
/// A database server in a container that two Tracon hosts share, and the schema each test gets on it.
/// </summary>
/// <remarks>
/// <para>
/// One container per test CLASS, started when the first test of the class runs and removed when the last one is done.
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
public sealed class SqlServerTwoNodeServer : TwoNodeServer
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .WithCleanUp(true)
        .Build();

    /// <inheritdoc />
    public override string Name => "SQL Server";

    /// <inheritdoc />
    public override TwoNodeDatabase NewDatabase()
        => new SqlServerTwoNodeDatabase(_container.GetConnectionString(), NewSchemaName());

    /// <inheritdoc />
    public override async Task<TwoNodeDatabase> NewDatabaseForLoginAsync(string password)
    {
        var login = "canary_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..10];

        await using var connection = new SqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE LOGIN [{login}] WITH PASSWORD = '{password}', CHECK_POLICY = OFF; ALTER SERVER ROLE sysadmin ADD MEMBER [{login}];";
        await command.ExecuteNonQueryAsync();

        var connectionString = new SqlConnectionStringBuilder(_container.GetConnectionString()) { UserID = login, Password = password }.ConnectionString;

        return new SqlServerTwoNodeDatabase(connectionString, NewSchemaName());
    }

    /// <inheritdoc />
    public override async ValueTask InitializeAsync() => await _container.StartAsync();

    /// <inheritdoc />
    public override async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
