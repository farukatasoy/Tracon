namespace AgentPrism;

/// <summary>
/// Sets up the <see cref="SqlServerDataSource"/> instance AgentPrism uses.
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
    /// <exception cref="AgentPrismException">The connection string is not defined.</exception>
    public static SqlServerDataSource Create(AgentPrismSqlServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new AgentPrismException(
                "The SQL Server connection string is not defined. Give it in the " +
                $"`UseSqlServer(connectionString)` call, or define the " +
                $"'{AgentPrismSqlServerOptions.SectionName}:{nameof(AgentPrismSqlServerOptions.ConnectionString)}' " +
                "setting in `dotnet user-secrets`.");
        }

        return new SqlServerDataSource(options.ConnectionString);
    }
}
