using System.Data.Common;

namespace AgentPrism;

/// <summary>
/// Builds the <see cref="SqliteDataSource"/> instance AgentPrism uses.
/// </summary>
/// <remarks>
/// A single data source is used and lives as a singleton in DI.
/// </remarks>
internal static class SqliteDataSourceFactory
{
    /// <summary>Builds a data source from settings.</summary>
    /// <param name="options">SQLite settings.</param>
    /// <returns>A data source ready for use.</returns>
    /// <exception cref="AgentPrismException">The connection string is not defined.</exception>
    public static SqliteDataSource Create(AgentPrismSqliteOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new AgentPrismException(
                "The SQLite connection string is not defined. Provide it in the `UseSqlite(connectionString)` call, " +
                $"or define the '{AgentPrismSqliteOptions.SectionName}:{nameof(AgentPrismSqliteOptions.ConnectionString)}' " +
                "setting in configuration.");
        }

        return new SqliteDataSource(options.ConnectionString);
    }

    /// <summary>
    /// Resolves the data source AgentPrism uses: <see cref="AgentPrismSqliteOptions.DataSource"/>
    /// when the consumer gave one, or a new one built from
    /// <see cref="AgentPrismSqliteOptions.ConnectionString"/> otherwise.
    /// </summary>
    /// <param name="options">SQLite settings.</param>
    /// <returns>The data source, and whether AgentPrism owns it (and must dispose it).</returns>
    /// <exception cref="AgentPrismException">The connection string is not defined.</exception>
    public static (DbDataSource DataSource, bool OwnsDataSource) Resolve(AgentPrismSqliteOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.DataSource is { } dataSource
            ? (dataSource, false)
            : (Create(options), true);
    }
}
