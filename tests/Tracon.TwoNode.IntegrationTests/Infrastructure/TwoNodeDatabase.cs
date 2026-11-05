using System.Data.Common;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace Tracon.TwoNode.IntegrationTests.Infrastructure;

/// <summary>
/// One schema on a shared server, as the two hosts of a test see it: how each of them is pointed at it.
/// </summary>
public abstract class TwoNodeDatabase
{
    /// <summary>Gets the connection string the hosts use.</summary>
    public abstract string ConnectionString { get; }

    /// <summary>Points a host's Tracon chain at the database.</summary>
    /// <param name="builder">The chain of one host.</param>
    public void Use(ITraconBuilder builder) => Use(builder, ConnectionString, applyMigrations: true);

    /// <summary>Points a host's Tracon chain at the schema through another connection string.</summary>
    /// <param name="builder">The chain of one host.</param>
    /// <param name="connectionString">The connection string: another password, another server.</param>
    /// <param name="applyMigrations">Whether the host migrates the schema when it starts.</param>
    public abstract void Use(ITraconBuilder builder, string connectionString, bool applyMigrations);

    /// <summary>Gets the connection string with its password replaced.</summary>
    /// <param name="password">The password.</param>
    /// <returns>The connection string.</returns>
    public abstract string WithPassword(string password);

    /// <summary>Gets a connection string with this schema's login and password for a server nothing listens on.</summary>
    /// <param name="password">The password.</param>
    /// <returns>The connection string.</returns>
    public abstract string Unreachable(string password);

    /// <summary>
    /// Reads every text, JSON and binary column of every table of the schema, as text, one value per line.
    /// </summary>
    /// <returns>The contents of the database.</returns>
    public abstract Task<string> DumpTextAsync();

    /// <summary>
    /// Writes a value into a table of its own in the schema, once into a text column, once into a JSON column and once
    /// into a binary column: what a search of the database is checked against.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>A task that completes when the row is written.</returns>
    public abstract Task PlantAsync(string value);

    /// <summary>Counts the rows of a table of the schema, through a new connection.</summary>
    /// <param name="table">The table, without its schema.</param>
    /// <returns>The number of rows.</returns>
    public abstract Task<long> CountRowsAsync(string table);

    /// <summary>
    /// Makes a run the row an older node writes when it starts a run: it has never beaten (no <c>heartbeat_at</c>) and it
    /// started at <paramref name="startedAt"/> on that node's clock. A run this version starts is marked when it starts.
    /// </summary>
    /// <param name="runId">The run, already started.</param>
    /// <param name="startedAt">When the older node started it.</param>
    /// <returns>A task that completes when the row is written.</returns>
    public abstract Task ClearRunHeartbeatAsync(Guid runId, DateTimeOffset startedAt);

    /// <summary>Reads the columns of the schema that can hold text and the rows of each.</summary>
    /// <param name="connection">An open connection to the database.</param>
    /// <param name="schema">The schema.</param>
    /// <param name="quote">Quotes an identifier.</param>
    /// <param name="textTypes">The data types that hold text or bytes.</param>
    /// <returns>Every value, one per line, each prefixed with table and column.</returns>
    protected static async Task<string> DumpAsync(DbConnection connection, string schema, Func<string, string> quote, IReadOnlySet<string> textTypes)
    {
        var columns = new List<(string Table, string Column)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT table_name, column_name, data_type FROM information_schema.columns WHERE table_schema = '"
                + schema.Replace("'", "''", StringComparison.Ordinal) + "' ORDER BY table_name, ordinal_position";

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                if (textTypes.Contains(reader.GetString(2)))
                {
                    columns.Add((reader.GetString(0), reader.GetString(1)));
                }
            }
        }

        var dump = new StringBuilder();

        foreach (var table in columns.GroupBy(static column => column.Table, StringComparer.Ordinal))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {string.Join(", ", table.Select(column => quote(column.Column)))} FROM {quote(schema)}.{quote(table.Key)}";

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                for (var index = 0; index < table.Count(); index++)
                {
                    if (reader.IsDBNull(index))
                    {
                        continue;
                    }

                    var value = reader.GetValue(index);
                    var name = table.Key + "." + table.ElementAt(index).Column;

                    if (value is byte[] bytes)
                    {
                        // Bytes are read both ways a text is stored in them: the value is searched as UTF-8 and as UTF-16.
                        dump.Append(name).Append('=').AppendLine(Encoding.UTF8.GetString(bytes));
                        dump.Append(name).Append("(utf16)=").AppendLine(Encoding.Unicode.GetString(bytes));
                    }
                    else
                    {
                        dump.Append(name).Append('=').AppendLine(Convert.ToString(value, CultureInfo.InvariantCulture));
                    }
                }
            }
        }

        return dump.ToString();
    }
}

/// <summary>A schema on the PostgreSQL container.</summary>
/// <param name="connectionString">The connection string of the login that owns the schema.</param>
/// <param name="schemaName">The schema this test owns.</param>
internal sealed class PostgreSqlTwoNodeDatabase(string connectionString, string schemaName) : TwoNodeDatabase
{
    private static readonly HashSet<string> TextTypes = new(StringComparer.Ordinal) { "text", "character varying", "character", "json", "jsonb", "bytea", "name" };

    /// <inheritdoc />
    public override string ConnectionString => connectionString;

    /// <inheritdoc />
    public override void Use(ITraconBuilder builder, string connectionString, bool applyMigrations)
        => builder.UsePostgreSql(options =>
        {
            options.ConnectionString = connectionString;
            options.AutoApplyMigrations = applyMigrations;
            options.SchemaName = schemaName;
        });

    /// <inheritdoc />
    public override string WithPassword(string password)
        => new NpgsqlConnectionStringBuilder(connectionString) { Password = password }.ConnectionString;

    /// <inheritdoc />
    public override string Unreachable(string password)
        => new NpgsqlConnectionStringBuilder(connectionString) { Host = "127.0.0.1", Port = 1, Password = password, Timeout = 1 }.ConnectionString;

    /// <inheritdoc />
    public override async Task PlantAsync(string value)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE TABLE \"{schemaName}\".canary_plant (a text, b jsonb, c bytea); "
            + $"INSERT INTO \"{schemaName}\".canary_plant VALUES (@v, to_jsonb(@v::text), convert_to(@v, 'UTF8'));";
        command.Parameters.AddWithValue("v", value);
        await command.ExecuteNonQueryAsync();
    }

    /// <inheritdoc />
    public override async Task<long> CountRowsAsync(string table)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{schemaName}\".\"{table}\"";

        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public override async Task ClearRunHeartbeatAsync(Guid runId, DateTimeOffset startedAt)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE \"{schemaName}\".runs SET heartbeat_at = NULL, started_at = @started WHERE id = @id";
        command.Parameters.AddWithValue("started", startedAt);
        command.Parameters.AddWithValue("id", runId);
        await command.ExecuteNonQueryAsync();
    }

    /// <inheritdoc />
    public override async Task<string> DumpTextAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        return await DumpAsync(connection, schemaName, static name => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"", TextTypes);
    }
}

/// <summary>A schema on the SQL Server container.</summary>
/// <param name="connectionString">The connection string of the login that owns the schema.</param>
/// <param name="schemaName">The schema this test owns.</param>
internal sealed class SqlServerTwoNodeDatabase(string connectionString, string schemaName) : TwoNodeDatabase
{
    private static readonly HashSet<string> TextTypes = new(StringComparer.Ordinal) { "nvarchar", "varchar", "nchar", "char", "text", "ntext", "varbinary", "xml" };

    /// <inheritdoc />
    public override string ConnectionString => connectionString;

    /// <inheritdoc />
    public override void Use(ITraconBuilder builder, string connectionString, bool applyMigrations)
        => builder.UseSqlServer(options =>
        {
            options.ConnectionString = connectionString;
            options.AutoApplyMigrations = applyMigrations;
            options.SchemaName = schemaName;
        });

    /// <inheritdoc />
    public override string WithPassword(string password)
        => new SqlConnectionStringBuilder(connectionString) { Password = password }.ConnectionString;

    /// <inheritdoc />
    public override string Unreachable(string password)
        => new SqlConnectionStringBuilder(connectionString) { DataSource = "127.0.0.1,1", Password = password, ConnectTimeout = 1 }.ConnectionString;

    /// <inheritdoc />
    public override async Task PlantAsync(string value)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE TABLE [{schemaName}].canary_plant (a nvarchar(max), b nvarchar(max), c varbinary(max)); "
            + $"INSERT INTO [{schemaName}].canary_plant VALUES (@v, N'{{\"v\":\"' + @v + N'\"}}', CAST(@v AS varbinary(max)));";
        command.Parameters.AddWithValue("@v", value);
        await command.ExecuteNonQueryAsync();
    }

    /// <inheritdoc />
    public override async Task<long> CountRowsAsync(string table)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM [{schemaName}].[{table}]";

        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public override async Task ClearRunHeartbeatAsync(Guid runId, DateTimeOffset startedAt)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE [{schemaName}].runs SET heartbeat_at = NULL, started_at = @started WHERE id = @id";
        command.Parameters.AddWithValue("@started", startedAt);
        command.Parameters.AddWithValue("@id", runId);
        await command.ExecuteNonQueryAsync();
    }

    /// <inheritdoc />
    public override async Task<string> DumpTextAsync()
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        return await DumpAsync(connection, schemaName, static name => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]", TextTypes);
    }
}
