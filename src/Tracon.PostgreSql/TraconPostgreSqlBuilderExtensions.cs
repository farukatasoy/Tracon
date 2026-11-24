using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Tracon;

/// <summary>Extensions that add PostgreSQL persistence to the Tracon chain.</summary>
public static class TraconPostgreSqlBuilderExtensions
{
    /// <summary>The provider's name in diagnostics and in the persistence registration.</summary>
    private const string ProviderDisplayName = "PostgreSQL";

    /// <summary>Enables PostgreSQL persistence given a connection string.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="connectionString"/> is empty.</exception>
    /// <remarks>
    /// <para>
    /// The schema is created and migrated at startup; no separate migration
    /// step is needed.
    /// </para>
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
    ///        .UseOpenAI(builder.Configuration.GetSection(OpenAIProviderOptions.SectionName))
    ///        .UsePostgreSql(builder.Configuration.GetConnectionString("Tracon")!);
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder UsePostgreSql(this ITraconBuilder builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return builder.UsePostgreSql(options => options.ConnectionString = connectionString);
    }

    /// <summary>
    /// Enables PostgreSQL persistence, reading settings from the <c>Tracon:PostgreSql</c> section.
    /// </summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="configurationSection">
    /// The section settings are read from. Usually
    /// <c>configuration.GetSection(TraconPostgreSqlOptions.SectionName)</c>.
    /// </param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    public static ITraconBuilder UsePostgreSql(
        this ITraconBuilder builder,
        IConfiguration configurationSection)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return builder.UsePostgreSql(options => Bind(configurationSection, options));
    }

    /// <summary>Enables PostgreSQL persistence with settings given in code.</summary>
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
    /// Overwriting is correct here because <c>UsePostgreSql()</c> is the
    /// consumer's <strong>explicit</strong> choice. The "register with TryAdd"
    /// rule is for Tracon's defaults, not for explicit calls.
    /// </para>
    /// </remarks>
    public static ITraconBuilder UsePostgreSql(
        this ITraconBuilder builder,
        Action<TraconPostgreSqlOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var services = builder.Services;

        UnreadableConfigurationValidator.Add<TraconPostgreSqlOptions>(services);
        services.Configure(configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<TraconPostgreSqlOptions>,
            TraconPostgreSqlOptionsValidator>());

        // Every store and service that does not depend on the database is registered
        // by the shared helper; only the store context and how the state is shared
        // between processes differ here.
        services.AddSqlPersistence(
            ProviderDisplayName,
            CreateStoreContext,
            // The state lives in a database server, so every instance that reaches it shares the state.
            ServiceDescriptor.Singleton(StateSharing.DatabaseServer(ProviderDisplayName)));

        // Vector-based semantic search. The ONLY concrete implementation. It fills
        // the slot Tracon reads availability from, and registers no IVectorSearchStore
        // of its own: the store exists only while EnableKnowledge is on, which IOptions
        // decides after this call, and a factory that returns null for "off" breaks
        // every container but Microsoft's (Autofac throws on the first agent compile).
        //
        // A consumer's own IVectorSearchStore (for SQL Server/SQLite) still wins.
        // With knowledge off the slot is empty, so an agent definition requesting
        // EnableVectorSearch fails compilation with a clear error
        // (TraconServiceCollectionExtensions) instead of the
        // "document_embeddings does not exist" database error the store
        // would otherwise hit, because the "knowledge" migration set never applied.
        services.Replace(ServiceDescriptor.Singleton(static provider => new VectorSearchStoreSlot(
            provider.GetService<IVectorSearchStore>() ?? CreateVectorSearchStore(provider))));

        return builder;
    }

    /// <summary>Builds the PostgreSQL vector store, or nothing while knowledge is off.</summary>
    /// <param name="provider">The service provider the store's dependencies are resolved from.</param>
    /// <returns>The store, or <see langword="null"/> while <see cref="TraconPostgreSqlOptions.EnableKnowledge"/> is off.</returns>
    private static PgVectorSearchStore? CreateVectorSearchStore(IServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptions<TraconPostgreSqlOptions>>().Value;

        if (!options.EnableKnowledge)
        {
            return null;
        }

        // The shared context's DataSource is always an NpgsqlDataSource for
        // this provider (NpgsqlDataSourceFactory.Resolve enforces it above);
        // reusing the context avoids building a second data source (and, when
        // the consumer supplied one, a second pool) just for this store, and
        // gives the upload the deadlock retry the other stores have.
        return new PgVectorSearchStore(
            provider.GetRequiredService<SqlStoreContext>(),
            options,
            provider.GetRequiredService<IOptions<TraconKnowledgeOptions>>().Value);
    }

    /// <summary>Builds the shared store layer's context from this provider's settings.</summary>
    /// <param name="provider">The service provider the context is resolved from.</param>
    /// <returns>The context every shared store reads.</returns>
    private static SqlStoreContext CreateStoreContext(IServiceProvider provider)
    {
        // Everything provider-specific is collected here; the stores never see an
        // Npgsql type.
        //
        // The data source is resolved HERE, not registered as its own
        // public NpgsqlDataSource DI service. Doing so used to be a two-way race:
        // either the consumer's own NpgsqlDataSource registration silently won
        // (TryAddSingleton) and Tracon's ConnectionString became a dead
        // setting, or Tracon's own instance leaked into the consumer's DI
        // container under a public Npgsql type. The only way in is now the
        // explicit TraconPostgreSqlOptions.DataSource field, and ownership
        // (whether Tracon may dispose it) travels with the context itself.
        var options = provider.GetRequiredService<IOptions<TraconPostgreSqlOptions>>().Value;
        var knowledgeOptions = provider.GetRequiredService<IOptions<TraconKnowledgeOptions>>().Value;
        var contentProtector = provider.GetRequiredService<IContentProtector>();
        var contentProtectionOptions = provider.GetRequiredService<IOptions<TraconContentProtectionOptions>>().Value;
        var (dataSource, ownsDataSource) = NpgsqlDataSourceFactory.Resolve(
            options, provider.GetService<ILoggerFactory>());

        return new SqlStoreContext
        {
            DataSource = dataSource,
            OwnsDataSource = ownsDataSource,
            Dialect = new PostgresDialect(options.SchemaName),
            CommandTimeoutSeconds = options.CommandTimeoutSeconds,
            AutoApplyMigrations = options.AutoApplyMigrations,
            ProviderName = ProviderDisplayName,
            ContentProtector = contentProtector,
            // Empty unless the protector is actually enabled -
            // The zero-surprise rule (identical behavior with the feature off) does not depend
            // on what TraconContentProtectionOptions.Columns holds.
            ProtectedColumns = contentProtector.IsEnabled
                ? (IReadOnlySet<ProtectedColumn>)contentProtectionOptions.Columns
                : System.Collections.Immutable.ImmutableHashSet<ProtectedColumn>.Empty,
            // The {dimension} placeholder of the knowledge set's
            // 0001_vector migration. Kept SEPARATE from the fixed schema
            // placeholder (schema) because it comes from
            // TraconKnowledgeOptions at startup, not headed by the provider.
            MigrationTemplateValues = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dimension"] = knowledgeOptions.Dimensions.ToString(CultureInfo.InvariantCulture),
            },
            // The "knowledge" set needs pgvector and is opt-in.
            // The "views" set publishes runs_v1 and is opt-in for
            // the same reason -- a published view is a permanent contract.
            EnabledMigrationSets = BuildEnabledMigrationSets(options),
        };
    }

    /// <summary>Collects the optional migration sets this options instance turns on.</summary>
    /// <param name="options">The provider's settings.</param>
    /// <returns>The set names <see cref="MigrationRunner"/> applies in addition to the core set.</returns>
    private static HashSet<string> BuildEnabledMigrationSets(TraconPostgreSqlOptions options)
    {
        var sets = new HashSet<string>(StringComparer.Ordinal);

        if (options.EnableKnowledge)
        {
            sets.Add("knowledge");
        }

        if (options.EnableReadViews)
        {
            sets.Add("views");
        }

        return sets;
    }

    /// <summary>
    /// Binds the configuration section to the settings object by hand.
    /// </summary>
    /// <remarks>
    /// <c>Bind()</c> relies on reflection and produces <c>IL2026</c> + <c>IL3050</c>.
    /// This method must also be updated when a new setting is added.
    /// </remarks>
    private static void Bind(IConfiguration section, TraconPostgreSqlOptions options)
    {
        var read = ConfigurationValueReader.For(section, options);

        if (section[nameof(TraconPostgreSqlOptions.ConnectionString)] is { Length: > 0 } connectionString)
        {
            options.ConnectionString = connectionString;
        }

        if (section[nameof(TraconPostgreSqlOptions.SchemaName)] is { Length: > 0 } schemaName)
        {
            options.SchemaName = schemaName;
        }

        read.Boolean(
            nameof(TraconPostgreSqlOptions.AutoApplyMigrations),
            static o => o.AutoApplyMigrations,
            static (o, v) => o.AutoApplyMigrations = v);

        read.Int32(
            nameof(TraconPostgreSqlOptions.CommandTimeoutSeconds),
            static o => o.CommandTimeoutSeconds,
            static (o, v) => o.CommandTimeoutSeconds = v);

        read.Boolean(
            nameof(TraconPostgreSqlOptions.EnableKnowledge),
            static o => o.EnableKnowledge,
            static (o, v) => o.EnableKnowledge = v);

        read.Boolean(
            nameof(TraconPostgreSqlOptions.EnableReadViews),
            static o => o.EnableReadViews,
            static (o, v) => o.EnableReadViews = v);
    }
}
