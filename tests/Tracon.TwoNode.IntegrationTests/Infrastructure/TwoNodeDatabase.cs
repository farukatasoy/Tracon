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
                    var text = value is byte[] bytes ? Encoding.UTF8.GetString(bytes) : Convert.ToString(value, CultureInfo.InvariantCulture);

                    dump.Append(table.Key).Append('.').Append(table.ElementAt(index).Column).Append('=').AppendLine(text);
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
    public override async Task<string> DumpTextAsync()
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        return await DumpAsync(connection, schemaName, static name => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]", TextTypes);
    }
}
