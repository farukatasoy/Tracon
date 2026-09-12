using System.Data.Common;

namespace Tracon;

/// <summary>
/// Builds the <see cref="SqliteDataSource"/> instance Tracon uses.
/// </summary>
/// <remarks>
/// A single data source is used and lives as a singleton in DI.
/// </remarks>
internal static class SqliteDataSourceFactory
{
    /// <summary>Builds a data source from settings.</summary>
    /// <param name="options">SQLite settings.</param>
    /// <returns>A data source ready for use.</returns>
    /// <exception cref="TraconException">The connection string is not defined.</exception>
    public static SqliteDataSource Create(TraconSqliteOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new TraconException(
                "The SQLite connection string is not defined. Provide it in the `UseSqlite(connectionString)` call, " +
                $"or define the '{TraconSqliteOptions.SectionName}:{nameof(TraconSqliteOptions.ConnectionString)}' " +
                "setting in configuration.");
        }

        return new SqliteDataSource(options.ConnectionString);
    }

    /// <summary>
    /// Resolves the data source Tracon uses: <see cref="TraconSqliteOptions.DataSource"/>
    /// when the consumer gave one, or a new one built from
    /// <see cref="TraconSqliteOptions.ConnectionString"/> otherwise.
    /// </summary>
    /// <param name="options">SQLite settings.</param>
    /// <returns>The data source, and whether Tracon owns it (and must dispose it).</returns>
    /// <exception cref="TraconException">The connection string is not defined.</exception>
    public static (DbDataSource DataSource, bool OwnsDataSource) Resolve(TraconSqliteOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.DataSource is { } dataSource
            ? (dataSource, false)
            : (Create(options), true);
    }
}
