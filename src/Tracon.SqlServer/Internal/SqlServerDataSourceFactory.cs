using System.Data.Common;

namespace Tracon;

/// <summary>
/// Sets up the <see cref="SqlServerDataSource"/> instance Tracon uses.
/// </summary>
/// <remarks>
/// A single data source is used and lives as a singleton in DI.
/// <c>Microsoft.Data.SqlClient</c> manages its own connection pool; no extra
/// pooling layer is added.
/// </remarks>
internal static class SqlServerDataSourceFactory
{
    /// <summary>Creates a data source from settings.</summary>
    /// <param name="options">The SQL Server settings.</param>
    /// <returns>A ready-to-use data source.</returns>
    /// <exception cref="TraconException">The connection string is not defined.</exception>
    public static SqlServerDataSource Create(TraconSqlServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new TraconException(
                "The SQL Server connection string is not defined. Give it in the " +
                $"`UseSqlServer(connectionString)` call, or define the " +
                $"'{TraconSqlServerOptions.SectionName}:{nameof(TraconSqlServerOptions.ConnectionString)}' " +
                "setting in `dotnet user-secrets`.");
        }

        return new SqlServerDataSource(options.ConnectionString);
    }

    /// <summary>
    /// Resolves the data source Tracon uses: <see cref="TraconSqlServerOptions.DataSource"/>
    /// when the consumer gave one, or a new one built from
    /// <see cref="TraconSqlServerOptions.ConnectionString"/> otherwise.
    /// </summary>
    /// <param name="options">The SQL Server settings.</param>
    /// <returns>The data source, and whether Tracon owns it (and must dispose it).</returns>
    /// <exception cref="TraconException">The connection string is not defined.</exception>
    public static (DbDataSource DataSource, bool OwnsDataSource) Resolve(TraconSqlServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.DataSource is { } dataSource
            ? (dataSource, false)
            : (Create(options), true);
    }
}
