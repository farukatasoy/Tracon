using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace Tracon;

/// <summary>
/// A <see cref="DbDataSource"/> adapter for <see cref="Microsoft.Data.SqlClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// Npgsql provides its own <c>NpgsqlDataSource</c> type; <c>Microsoft.Data.SqlClient</c>
/// <strong>does not offer</strong> a <see cref="DbDataSource"/> implementation.
/// Since the shared store layer knows the data source through this base type,
/// a thin adapter is written.
/// </para>
/// <para>
/// The connection pool is <c>SqlClient</c>'s own pool; there is no extra
/// pooling layer here. The base class's <see cref="DbDataSource.CreateCommand(string)"/>
/// implementation takes a connection from the pool when a command is run and
/// returns it when the command is disposed — the same contract as
/// <c>NpgsqlDataSource</c>.
/// </para>
/// </remarks>
internal sealed class SqlServerDataSource : DbDataSource
{
    private readonly string _connectionString;
    private string? _redactedConnectionString;

    /// <summary>Creates a new data source.</summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
    public SqlServerDataSource(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _connectionString = connectionString;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The password is removed, matching the <c>NpgsqlDataSource.ConnectionString</c>
    /// contract: whatever reads this property — a diagnostics page, a log line —
    /// must not receive a credential. The connections this source creates still
    /// use the full string.
    /// </remarks>
    public override string ConnectionString => _redactedConnectionString ??= Redact(_connectionString);

    private static string Redact(string connectionString)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            builder.Remove("Password");
            return builder.ConnectionString;
        }
        catch (ArgumentException)
        {
            // Not parseable as a SQL Server connection string; SqlConnection
            // rejects it too, so there is no working credential to protect.
            return connectionString;
        }
    }

    /// <inheritdoc />
    protected override DbConnection CreateDbConnection() => new SqlConnection(_connectionString);
}
