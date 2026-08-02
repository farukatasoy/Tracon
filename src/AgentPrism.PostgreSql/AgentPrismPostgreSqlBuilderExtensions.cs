using System.Globalization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AgentPrism;

/// <summary>AgentPrism zincirine PostgreSQL kaliciligini ekleyen uzantilar.</summary>
public static class AgentPrismPostgreSqlBuilderExtensions
{
    /// <summary>Baglanti dizesi vererek PostgreSQL kaliciligini acar.</summary>
    /// <param name="builder">AgentPrism zinciri.</param>
    /// <param name="connectionString">PostgreSQL baglanti dizesi.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> <see langword="null"/> ise.</exception>
    /// <exception cref="ArgumentException"><paramref name="connectionString"/> bos ise.</exception>
    public static IAgentPrismBuilder UsePostgreSql(this IAgentPrismBuilder builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return builder.UsePostgreSql(options => options.ConnectionString = connectionString);
    }

    /// <summary>
    /// Ayarlari <c>AgentPrism:PostgreSql</c> bolumunden okuyarak PostgreSQL kaliciligini acar.
    /// </summary>
    /// <param name="builder">AgentPrism zinciri.</param>
    /// <param name="configurationSection">
    /// Ayarlarin okunacagi bolum. Genellikle
    /// <c>configuration.GetSection(AgentPrismPostgreSqlOptions.SectionName)</c>.
    /// </param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException">Parametrelerden biri <see langword="null"/> ise.</exception>
    public static IAgentPrismBuilder UsePostgreSql(
        this IAgentPrismBuilder builder,
        IConfiguration configurationSection)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return builder.UsePostgreSql(options => Bind(configurationSection, options));
    }

    /// <summary>Ayarlari kodda vererek PostgreSQL kaliciligini acar.</summary>
    /// <param name="builder">AgentPrism zinciri.</param>
    /// <param name="configure">Ayar degistirici.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException">Parametrelerden biri <see langword="null"/> ise.</exception>
    /// <remarks>
    /// <para>
    /// Depolar <c>TryAdd</c> ile degil <see cref="ServiceCollectionDescriptorExtensions.Replace"/>
    /// ile kaydedilir. Sebep: <c>AddAgentPrism()</c> bellek ici depolari zaten
    /// <c>TryAddSingleton</c> ile kaydetmis olur ve zincirde <em>once</em> calisir;
    /// bu cagrida <c>TryAdd</c> kullanmak sessizce hicbir sey yapmazdi.
    /// </para>
    /// <para>
    /// Uzerine yazma burada dogrudur cunku <c>UsePostgreSql()</c> tuketicinin
    /// <strong>acik</strong> tercihidir. "TryAdd ile kaydet" kurali AgentPrism'in
    /// varsayilanlari icindir, acik cagrilar icin degil.
    /// Gerekce: <c>docs/KARARLAR.md</c>, karar K-025.
    /// </para>
    /// </remarks>
    public static IAgentPrismBuilder UsePostgreSql(
        this IAgentPrismBuilder builder,
        Action<AgentPrismPostgreSqlOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var services = builder.Services;

        services.AddOptions<AgentPrismPostgreSqlOptions>().ValidateOnStart();
        services.Configure(configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<AgentPrismPostgreSqlOptions>,
            AgentPrismPostgreSqlOptionsValidator>());

        // Tek veri kaynagi; Npgsql havuzu kendi yonetir.
        services.TryAddSingleton(static provider => NpgsqlDataSourceFactory.Create(
            provider.GetRequiredService<IOptions<AgentPrismPostgreSqlOptions>>().Value,
            provider.GetService<ILoggerFactory>()));

        services.TryAddSingleton<MigrationRunner>();
        services.AddHostedService<MigrationHostedService>();

        // Bellek ici depolarin yerini alir. TryAdd burada ise yaramaz.
        services.Replace(ServiceDescriptor.Singleton<IAgentDefinitionStore, PostgresAgentDefinitionStore>());
        services.Replace(ServiceDescriptor.Singleton<IRunStore, PostgresRunStore>());
        services.Replace(ServiceDescriptor.Singleton<ISessionStore, PostgresSessionStore>());
        services.Replace(ServiceDescriptor.Singleton<ITraceStore, PostgresTraceStore>());
        services.Replace(ServiceDescriptor.Singleton<IToolApprovalRuleStore, PostgresToolApprovalRuleStore>());
        services.Replace(ServiceDescriptor.Singleton<IMcpServerStore, PostgresMcpServerStore>());
        services.Replace(ServiceDescriptor.Singleton<ITenantStore, PostgresTenantStore>());

        // Sohbet gecmisi. AgentDefinitionCompiler bunu derledigi her agent'a baglar;
        // kayitli degilse MAF'in bellek ici varsayilani kullanilir.
        services.Replace(ServiceDescriptor.Singleton<ChatHistoryProvider, PostgresChatHistoryProvider>());

        return builder;
    }

    /// <summary>
    /// Yapilandirma bolumunu ayar nesnesine elle baglar.
    /// </summary>
    /// <remarks>
    /// <c>Bind()</c> yansimaya dayanir ve <c>IL2026</c> + <c>IL3050</c> uretir.
    /// Yeni bir ayar eklendiginde bu metoda da eklenmelidir.
    /// Gerekce: <c>docs/KARARLAR.md</c>, karar K-021.
    /// </remarks>
    private static void Bind(IConfiguration section, AgentPrismPostgreSqlOptions options)
    {
        if (section[nameof(AgentPrismPostgreSqlOptions.ConnectionString)] is { Length: > 0 } connectionString)
        {
            options.ConnectionString = connectionString;
        }

        if (section[nameof(AgentPrismPostgreSqlOptions.SchemaName)] is { Length: > 0 } schemaName)
        {
            options.SchemaName = schemaName;
        }

        if (bool.TryParse(section[nameof(AgentPrismPostgreSqlOptions.AutoApplyMigrations)], out var autoApply))
        {
            options.AutoApplyMigrations = autoApply;
        }

        if (int.TryParse(
                section[nameof(AgentPrismPostgreSqlOptions.CommandTimeoutSeconds)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var commandTimeout))
        {
            options.CommandTimeoutSeconds = commandTimeout;
        }
    }
}
