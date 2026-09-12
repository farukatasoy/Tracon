using Microsoft.Extensions.Logging;
using Npgsql;

namespace Tracon;

/// <summary>
/// Sets up the <see cref="NpgsqlDataSource"/> instance Tracon uses.
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
/// generator (see <c>TraconJsonContext</c>).
/// </para>
/// </remarks>
internal static class NpgsqlDataSourceFactory
{
    /// <summary>Creates a data source from settings.</summary>
    /// <param name="options">The PostgreSQL settings.</param>
    /// <param name="loggerFactory">The logger factory Npgsql will use.</param>
    /// <returns>A ready-to-use data source.</returns>
    /// <exception cref="TraconException">The connection string is not defined.</exception>
    public static NpgsqlDataSource Create(TraconPostgreSqlOptions options, ILoggerFactory? loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new TraconException(
                "The PostgreSQL connection string is not defined. Give it in the " +
                $"`UsePostgreSql(connectionString)` call, or define the " +
                $"'{TraconPostgreSqlOptions.SectionName}:{nameof(TraconPostgreSqlOptions.ConnectionString)}' " +
                "setting in `dotnet user-secrets`.");
        }

        var builder = new NpgsqlDataSourceBuilder(options.ConnectionString);

        if (loggerFactory is not null)
        {
            builder.UseLoggerFactory(loggerFactory);
        }

        return builder.Build();
    }

    /// <summary>
    /// Resolves the data source Tracon uses: <see cref="TraconPostgreSqlOptions.DataSource"/>
    /// when the consumer gave one, or a new one built from
    /// <see cref="TraconPostgreSqlOptions.ConnectionString"/> otherwise.
    /// </summary>
    /// <param name="options">The PostgreSQL settings.</param>
    /// <param name="loggerFactory">The logger factory Npgsql will use, when Tracon builds its own data source.</param>
    /// <returns>The data source, and whether Tracon owns it (and must dispose it).</returns>
    /// <exception cref="TraconException">
    /// The connection string is not defined, or <see cref="TraconPostgreSqlOptions.DataSource"/>
    /// is set but is not an <see cref="NpgsqlDataSource"/>.
    /// </exception>
    public static (NpgsqlDataSource DataSource, bool OwnsDataSource) Resolve(
        TraconPostgreSqlOptions options,
        ILoggerFactory? loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.DataSource is null)
        {
            return (Create(options, loggerFactory), true);
        }

        if (options.DataSource is not NpgsqlDataSource npgsqlDataSource)
        {
            throw new TraconException(
                $"{nameof(TraconPostgreSqlOptions)}.{nameof(TraconPostgreSqlOptions.DataSource)} must be an " +
                $"{nameof(NpgsqlDataSource)} instance (built with {nameof(NpgsqlDataSourceBuilder)}). " +
                $"Actual type: '{options.DataSource.GetType()}'.");
        }

        return (npgsqlDataSource, false);
    }
}
