using Microsoft.Data.Sqlite;

namespace Tracon;

/// <summary>
/// Says where the state of a SQLite provider lives, from the connection string the host gave.
/// </summary>
/// <remarks>
/// A file is still there when the next process starts. A shared in-memory database (a supported setup, for tests and
/// for a host that keeps nothing) is not: it ends with the process, so a restart leaves no run behind.
/// </remarks>
internal static class SqliteStateSharing
{
    /// <summary>Returns the value to register for the options the host built.</summary>
    /// <param name="options">The SQLite options.</param>
    /// <returns>The state-sharing value for that database.</returns>
    public static StateSharing For(TraconSqliteOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // A data source the host supplied reports its own connection string; one that reports none is taken for a file,
        // the case that asks the question.
        var connectionString = options.DataSource is { } source ? source.ConnectionString : options.ConnectionString;

        return LivesInMemory(connectionString) ? StateSharing.SqliteMemory : StateSharing.SqliteFile;
    }

    /// <summary>Reports whether a connection string names a database that exists only in memory.</summary>
    /// <param name="connectionString">The connection string.</param>
    /// <returns><see langword="true"/> for <c>:memory:</c>, <c>Mode=Memory</c> and a URI with <c>mode=memory</c>.</returns>
    internal static bool LivesInMemory(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        SqliteConnectionStringBuilder builder;

        try
        {
            builder = new SqliteConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            // Another layer reports the invalid syntax when the connection is opened.
            return false;
        }

        var dataSource = builder.DataSource;

        if (builder.Mode == SqliteOpenMode.Memory
            || string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        const string UriPrefix = "file:";

        if (!dataSource.StartsWith(UriPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // file::memory:?cache=shared and file:<name>?mode=memory&cache=shared
        var uri = dataSource.AsSpan(UriPrefix.Length);
        var query = uri.IndexOf('?');
        var name = query < 0 ? uri : uri[..query];

        if (name.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (query < 0)
        {
            return false;
        }

        foreach (var pair in dataSource[(UriPrefix.Length + query + 1)..].Split('&'))
        {
            if (string.Equals(pair, "mode=memory", StringComparison.OrdinalIgnoreCase)
                || string.Equals(pair, "vfs=memdb", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
