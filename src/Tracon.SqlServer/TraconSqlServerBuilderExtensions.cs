using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Extensions that add SQL Server persistence to the Tracon chain.</summary>
public static class TraconSqlServerBuilderExtensions
{
    /// <summary>The provider's name in diagnostics and in the persistence registration.</summary>
    private const string ProviderDisplayName = "SQL Server";

    /// <summary>Enables SQL Server persistence given a connection string.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="connectionString"/> is empty.</exception>
    /// <remarks>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseSqlServer(builder.Configuration.GetConnectionString("Tracon")!);
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder UseSqlServer(this ITraconBuilder builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return builder.UseSqlServer(options => options.ConnectionString = connectionString);
    }

    /// <summary>
    /// Enables SQL Server persistence, reading settings from the <c>Tracon:SqlServer</c> section.
    /// </summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="configurationSection">
    /// The section settings are read from. Usually
    /// <c>configuration.GetSection(TraconSqlServerOptions.SectionName)</c>.
    /// </param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    public static ITraconBuilder UseSqlServer(
        this ITraconBuilder builder,
        IConfiguration configurationSection)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return builder.UseSqlServer(options => Bind(configurationSection, options));
    }

    /// <summary>Enables SQL Server persistence with settings given in code.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="configure">The settings mutator.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Stores are registered with <see cref="ServiceCollectionDescriptorExtensions.Replace"/>,
    /// not <c>TryAdd</c>. <c>AddTracon()</c> has already registered the
    /// in-memory stores with <c>TryAddSingleton</c> and runs <em>before</em> this
    /// call in the chain; using <c>TryAdd</c> here would silently do nothing.
    /// </para>
    /// <para>
    /// Overwriting is correct here because <c>UseSqlServer()</c> is the
    /// consumer's <strong>explicit</strong> choice. The "register with TryAdd"
    /// rule is for Tracon's defaults, not for explicit calls.
    /// </para>
    /// </remarks>
    public static ITraconBuilder UseSqlServer(
        this ITraconBuilder builder,
        Action<TraconSqlServerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var services = builder.Services;

        UnreadableConfigurationValidator.Add<TraconSqlServerOptions>(services);
        services.Configure(configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<TraconSqlServerOptions>,
            TraconSqlServerOptionsValidator>());

        // Every store and service that does not depend on the database is registered
        // by the shared helper; only the store context and how the state is shared
        // between processes differ here.
        services.AddSqlPersistence(
            ProviderDisplayName,
            CreateStoreContext,
            // The state lives in a database server, so every instance that reaches it shares the state.
            ServiceDescriptor.Singleton(StateSharing.DatabaseServer(ProviderDisplayName)));

        return builder;
    }

    /// <summary>Builds the shared store layer's context from this provider's settings.</summary>
    /// <param name="provider">The service provider the context is resolved from.</param>
    /// <returns>The context every shared store reads.</returns>
    private static SqlStoreContext CreateStoreContext(IServiceProvider provider)
    {
        // Everything provider-specific is collected here; the stores never see a
        // SqlClient type.
        //
        // The data source is resolved HERE, not registered as its own
        // public DI service — see the matching comment in
        // TraconPostgreSqlBuilderExtensions.CreateStoreContext for the rationale.
        var options = provider.GetRequiredService<IOptions<TraconSqlServerOptions>>().Value;
        var contentProtector = provider.GetRequiredService<IContentProtector>();
        var contentProtectionOptions = provider.GetRequiredService<IOptions<TraconContentProtectionOptions>>().Value;
        var (dataSource, ownsDataSource) = SqlServerDataSourceFactory.Resolve(options);

        return new SqlStoreContext
        {
            DataSource = dataSource,
            OwnsDataSource = ownsDataSource,
            Dialect = new SqlServerDialect(options.SchemaName),
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
    /// Binds the configuration section to the settings object by hand.
    /// </summary>
    /// <remarks>
    /// <c>Bind()</c> relies on reflection and produces <c>IL2026</c> + <c>IL3050</c>.
    /// This method must also be updated when a new setting is added.
    /// </remarks>
    private static void Bind(IConfiguration section, TraconSqlServerOptions options)
    {
        var read = ConfigurationValueReader.For(section, options);

        if (section[nameof(TraconSqlServerOptions.ConnectionString)] is { Length: > 0 } connectionString)
        {
            options.ConnectionString = connectionString;
        }

        if (section[nameof(TraconSqlServerOptions.SchemaName)] is { Length: > 0 } schemaName)
        {
            options.SchemaName = schemaName;
        }

        read.Boolean(
            nameof(TraconSqlServerOptions.AutoApplyMigrations),
            static o => o.AutoApplyMigrations,
            static (o, v) => o.AutoApplyMigrations = v);

        read.Int32(
            nameof(TraconSqlServerOptions.CommandTimeoutSeconds),
            static o => o.CommandTimeoutSeconds,
            static (o, v) => o.CommandTimeoutSeconds = v);

        read.Boolean(
            nameof(TraconSqlServerOptions.EnableReadViews),
            static o => o.EnableReadViews,
            static (o, v) => o.EnableReadViews = v);
    }
}
