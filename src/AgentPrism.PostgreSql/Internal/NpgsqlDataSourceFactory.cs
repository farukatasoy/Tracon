using Microsoft.Extensions.Logging;
using Npgsql;

namespace AgentPrism;

/// <summary>
/// Sets up the <see cref="NpgsqlDataSource"/> instance AgentPrism uses.
/// </summary>
/// <remarks>
/// <para>
/// A single data source is used and lives as a singleton in DI. Npgsql manages
/// its own connection pool; no extra pooling layer is added.
/// </para>
/// <para>
/// <c>EnableDynamicJson()</c> is <strong>not used</strong>: it relies on
/// reflection and breaks the AOT promise. <c>jsonb</c> fields are carried as
/// text; serialization is done on the application side with a source
/// generator (see <c>AgentPrismJsonContext</c>).
/// </para>
/// </remarks>
internal static class NpgsqlDataSourceFactory
{
    /// <summary>Creates a data source from settings.</summary>
    /// <param name="options">The PostgreSQL settings.</param>
    /// <param name="loggerFactory">The logger factory Npgsql will use.</param>
    /// <returns>A ready-to-use data source.</returns>
    /// <exception cref="AgentPrismException">The connection string is not defined.</exception>
    public static NpgsqlDataSource Create(AgentPrismPostgreSqlOptions options, ILoggerFactory? loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new AgentPrismException(
                "The PostgreSQL connection string is not defined. Give it in the " +
                $"`UsePostgreSql(connectionString)` call, or define the " +
                $"'{AgentPrismPostgreSqlOptions.SectionName}:{nameof(AgentPrismPostgreSqlOptions.ConnectionString)}' " +
                "setting in `dotnet user-secrets`.");
        }

        var builder = new NpgsqlDataSourceBuilder(options.ConnectionString);

        if (loggerFactory is not null)
        {
            builder.UseLoggerFactory(loggerFactory);
        }

        return builder.Build();
    }
}
