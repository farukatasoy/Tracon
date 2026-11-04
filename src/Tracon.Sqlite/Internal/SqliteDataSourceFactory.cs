using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;

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
            ? (new DecimalFunctionDataSource(dataSource), false)
            : (Create(options), true);
    }

    /// <summary>
    /// Stands in front of a data source the consumer supplied, so the connections Tracon opens from it carry the exact
    /// decimal functions the cost totals and the quota counter are written with.
    /// </summary>
    /// <remarks>
    /// The consumer's source is the one Tracon opens connections from, and the consumer keeps owning it: this wrapper holds
    /// nothing to release and never disposes it. A source that hands out a connection that is not a
    /// <see cref="SqliteConnection"/> (a profiling wrapper, say) cannot carry the functions; the statements that name them
    /// then fail with <c>no such function</c> and never fall back to a floating-point sum.
    /// </remarks>
    private sealed class DecimalFunctionDataSource(DbDataSource inner) : DbDataSource
    {
        /// <inheritdoc />
        public override string ConnectionString => inner.ConnectionString;

        /// <inheritdoc />
        protected override DbConnection CreateDbConnection()
        {
            var connection = inner.CreateConnection();

            if (connection is SqliteConnection sqlite)
            {
                sqlite.StateChange += static (sender, eventArgs) =>
                {
                    if (eventArgs.CurrentState == ConnectionState.Open && sender is SqliteConnection opened)
                    {
                        SqliteDecimalFunctions.Register(opened);
                    }
                };
            }

            return connection;
        }
    }
}
