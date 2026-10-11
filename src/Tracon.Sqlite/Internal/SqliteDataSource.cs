using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Tracon;

/// <summary>
/// A <see cref="DbDataSource"/> adapter for <see cref="Microsoft.Data.Sqlite"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>Npgsql</c> provides the <c>NpgsqlDataSource</c> type itself; <c>Microsoft.Data.Sqlite</c>
/// does <strong>not</strong> offer a <see cref="DbDataSource"/> implementation. Because the
/// shared store layer knows the data source only through this base type, a thin adapter is
/// written (same pattern as SQL Server).
/// </para>
/// <para>
/// <strong>WAL, <c>busy_timeout</c>, and foreign-key enforcement are set on EVERY NEW
/// connection</strong> via the connection's state-change event. These settings are done with
/// explicit <c>PRAGMA</c> commands, not connection-string keywords (no such keyword exists for
/// WAL or <c>busy_timeout</c>); they do not depend on the consumer's connection string.
/// </para>
/// </remarks>
internal sealed class SqliteDataSource : DbDataSource
{
    private readonly string _connectionString;
    private string? _redactedConnectionString;

    /// <summary>Creates a new data source.</summary>
    /// <param name="connectionString">The SQLite connection string.</param>
    public SqliteDataSource(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _connectionString = connectionString;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The password (SQLCipher) is removed, matching the
    /// <c>NpgsqlDataSource.ConnectionString</c> contract: whatever reads this
    /// property — a diagnostics page, a log line — must not receive a
    /// credential. The connections this source creates still use the full string.
    /// </remarks>
    public override string ConnectionString => _redactedConnectionString ??= Redact(_connectionString);

    private static string Redact(string connectionString)
    {
        try
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);
            builder.Remove("Password");
            return builder.ConnectionString;
        }
        catch (ArgumentException)
        {
            // Not parseable as a SQLite connection string; SqliteConnection
            // rejects it too, so there is no working credential to protect.
            return connectionString;
        }
    }

    /// <inheritdoc />
    protected override DbConnection CreateDbConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.StateChange += OnStateChange;

        return connection;
    }

    /// <summary>
    /// Sets WAL, <c>busy_timeout</c>, and foreign-key enforcement when a connection opens.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For <c>:memory:</c> databases, the <c>journal_mode=WAL</c> request silently falls back
    /// to <c>memory</c> mode (no error); the pragma is still run unconditionally.
    /// </para>
    /// <para>
    /// <c>busy_timeout</c> bounds ONE attempt. A statement refused with <c>SQLITE_BUSY</c>,
    /// <c>SQLITE_LOCKED</c> or <c>262</c> is then sent again by Microsoft.Data.Sqlite itself,
    /// 150 ms apart, until the command's <c>CommandTimeout</c> has passed, so a held write
    /// lock surfaces as <c>database is locked</c> only after that. Tracon adds no retry
    /// layer of its own; the behavior is pinned by <c>SqliteWriteContentionTests</c>.
    /// </para>
    /// </remarks>
    private static void OnStateChange(object? sender, StateChangeEventArgs eventArgs)
    {
        if (eventArgs.CurrentState != ConnectionState.Open || sender is not SqliteConnection connection)
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 5000; " +
            "PRAGMA case_sensitive_like = ON;";
        command.ExecuteNonQuery();
    }
}
