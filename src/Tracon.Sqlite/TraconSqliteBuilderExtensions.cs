using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Extensions that add SQLite persistence to the Tracon chain.</summary>
public static class TraconSqliteBuilderExtensions
{
    /// <summary>The provider's name in diagnostics and in the persistence registration.</summary>
    private const string ProviderDisplayName = "SQLite";

    /// <summary>Enables SQLite persistence by giving a connection string.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="connectionString">The SQLite connection string (example: <c>Data Source=tracon.db</c>).</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="connectionString"/> is empty.</exception>
    /// <remarks>
    /// <para>
    /// <strong>The job queue is shared.</strong> A SQL provider keeps the job queue in the database, and the job
    /// worker is on by default, so a process that connects to a database another process already uses and has no
    /// <c>Tracon:Scheduling</c> section joins that queue and leases its jobs. Set
    /// <c>Tracon:Scheduling:RunWorker</c> to <see langword="false"/> to keep a process that only takes requests out
    /// of it. The worker logs one line when it starts, and <c>GET /api/meta</c> reports it in
    /// <c>storage.jobWorkerEnabled</c>.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseSqlite("Data Source=tracon.db");
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder UseSqlite(this ITraconBuilder builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return builder.UseSqlite(options => options.ConnectionString = connectionString);
    }

    /// <summary>
    /// Enables SQLite persistence, reading settings from the <c>Tracon:Sqlite</c> section.
    /// </summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="configurationSection">
    /// The section settings are read from. Usually
    /// <c>configuration.GetSection(TraconSqliteOptions.SectionName)</c>.
    /// </param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    public static ITraconBuilder UseSqlite(
        this ITraconBuilder builder,
        IConfiguration configurationSection)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return builder.UseSqlite(options => Bind(configurationSection, options));
    }

    /// <summary>Enables SQLite persistence by giving settings in code.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="configure">The settings mutator.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    /// <remarks>
    /// Stores are registered with <see cref="ServiceCollectionDescriptorExtensions.Replace"/>,
    /// not <c>TryAdd</c>; the rationale is the same as for <c>UseSqlServer()</c>/<c>UsePostgreSql()</c>
    /// </remarks>
    public static ITraconBuilder UseSqlite(
        this ITraconBuilder builder,
        Action<TraconSqliteOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var services = builder.Services;

        UnreadableConfigurationValidator.Add<TraconSqliteOptions>(services);
        services.Configure(configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<TraconSqliteOptions>,
            TraconSqliteOptionsValidator>());

        // Every store and service that does not depend on the database is registered
        // by the shared helper; only the store context and how the state is shared
        // between processes differ here.
        services.AddSqlPersistence(
            ProviderDisplayName,
            CreateStoreContext,
            // A SQLite file serves one process: the production profile refuses it when the
            // deployment declares more than one instance. Whether the state is still there after a restart
            // depends on the connection string (a shared in-memory database dies with the process), which is
            // only known once the options are built, so the value is made when the profile asks for it.
            ServiceDescriptor.Singleton(static provider =>
                SqliteStateSharing.For(provider.GetRequiredService<IOptions<TraconSqliteOptions>>().Value)));

        return builder;
    }

    /// <summary>Builds the shared store layer's context from this provider's settings.</summary>
    /// <param name="provider">The service provider the context is resolved from.</param>
    /// <returns>The context every shared store reads.</returns>
    private static SqlStoreContext CreateStoreContext(IServiceProvider provider)
    {
        // Everything provider-specific is collected here; the stores never see the
        // Microsoft.Data.Sqlite type.
        //
        // The data source is resolved HERE, not registered as its own
        // public DI service — see the matching comment in
        // TraconPostgreSqlBuilderExtensions.CreateStoreContext for the rationale.
        var options = provider.GetRequiredService<IOptions<TraconSqliteOptions>>().Value;
        var contentProtector = provider.GetRequiredService<IContentProtector>();
        var contentProtectionOptions = provider.GetRequiredService<IOptions<TraconContentProtectionOptions>>().Value;
        var (dataSource, ownsDataSource) = SqliteDataSourceFactory.Resolve(options);

        return new SqlStoreContext
        {
            DataSource = dataSource,
            OwnsDataSource = ownsDataSource,
            Dialect = new SqliteDialect(options.TablePrefix),
            CommandTimeoutSeconds = options.CommandTimeoutSeconds,
            AutoApplyMigrations = options.AutoApplyMigrations,
            // The "views" set publishes runs_v1 and is opt-in --
            // a published view is a permanent data contract.
            EnabledMigrationSets = options.EnableReadViews
                ? new HashSet<string>(StringComparer.Ordinal) { "views" }
                : System.Collections.Immutable.ImmutableHashSet<string>.Empty,
            ProviderName = ProviderDisplayName,
            ContentProtector = contentProtector,
            // Empty unless the protector is actually enabled -
            // The zero-surprise rule (identical behavior with the feature off) does not depend
            // on what TraconContentProtectionOptions.Columns holds.
            ProtectedColumns = contentProtector.IsEnabled
                ? (IReadOnlySet<ProtectedColumn>)contentProtectionOptions.Columns
                : System.Collections.Immutable.ImmutableHashSet<ProtectedColumn>.Empty,
        };
    }

    /// <summary>
    /// Manually binds the configuration section into the settings object.
    /// </summary>
    /// <remarks>
    /// <c>Bind()</c> relies on reflection and produces <c>IL2026</c> + <c>IL3050</c>.
    /// This method must also be updated when a new setting is added.
    /// </remarks>
    private static void Bind(IConfiguration section, TraconSqliteOptions options)
    {
        var read = ConfigurationValueReader.For(section, options);

        if (section[nameof(TraconSqliteOptions.ConnectionString)] is { Length: > 0 } connectionString)
        {
            options.ConnectionString = connectionString;
        }

        if (section[nameof(TraconSqliteOptions.TablePrefix)] is { Length: > 0 } tablePrefix)
        {
            options.TablePrefix = tablePrefix;
        }

        read.Boolean(
            nameof(TraconSqliteOptions.AutoApplyMigrations),
            static o => o.AutoApplyMigrations,
            static (o, v) => o.AutoApplyMigrations = v);

        read.Int32(
            nameof(TraconSqliteOptions.CommandTimeoutSeconds),
            static o => o.CommandTimeoutSeconds,
            static (o, v) => o.CommandTimeoutSeconds = v);

        read.Boolean(
            nameof(TraconSqliteOptions.EnableReadViews),
            static o => o.EnableReadViews,
            static (o, v) => o.EnableReadViews = v);
    }
}
