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

        // Paylasilan depo katmaninin baglami. Saglayiciya ozgu her sey burada
        // toplanir; depolar Npgsql tipi gormez (Faz 23, K-176).
        // Depo kayitlariyla ayni kural: son cagri kazanir. TryAdd olsaydi ikinci
        // bir saglayici kaydedildiginde depolar yeni saglayiciya, baglam eskisine
        // bakardi ve ikisi sessizce ayrisirdi.
        services.Replace(ServiceDescriptor.Singleton(static provider =>
        {
            var options = provider.GetRequiredService<IOptions<AgentPrismPostgreSqlOptions>>().Value;

            return new SqlStoreContext
            {
                DataSource = provider.GetRequiredService<NpgsqlDataSource>(),
                Dialect = new PostgresDialect(options.SchemaName),
                CommandTimeoutSeconds = options.CommandTimeoutSeconds,
                AutoApplyMigrations = options.AutoApplyMigrations,
                ProviderName = "PostgreSQL",
            };
        }));

        // Isaret birikir (TryAdd degil): birden fazla saglayici kayitliysa
        // MigrationHostedService acilista uyarir. Gerekce: K-183.
        services.AddSingleton(new SqlPersistenceRegistration("PostgreSQL"));

        services.Replace(ServiceDescriptor.Singleton(static provider => new MigrationRunner(
            provider.GetRequiredService<SqlStoreContext>(),
            provider.GetRequiredService<ILogger<MigrationRunner>>())));
        services.AddHostedService<MigrationHostedService>();

        // Denetim izi defteri de bellek icinin yerini alir.
        services.Replace(ServiceDescriptor.Singleton<IAuditLog, SqlAuditLog>());

        // Bellek ici depolarin yerini alir. TryAdd burada ise yaramaz.
        //
        // Yazma yapan bes deponun tumu, Faz 9'un denetim izi dekoratorleriyle
        // sarilarak kaydedilir; boylece denetim izi bellek ici veya PostgreSQL
        // fark etmeksizin ayni sekilde calisir. Gerekce: AgentPrism.Core'daki
        // AddAgentPrism() kaydiyla ayni desen (docs/09-YONETISIM-VE-DENETIM-IZI.md).
        services.Replace(ServiceDescriptor.Singleton<IAgentDefinitionStore, AuditingAgentDefinitionStore>(
            static provider => new AuditingAgentDefinitionStore(
                ActivatorUtilities.CreateInstance<SqlAgentDefinitionStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<ITenantContext>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<ILogger<AuditingAgentDefinitionStore>>())));
        services.Replace(
            ServiceDescriptor.Singleton<IAgentSkillStore, SqlAgentSkillStore>());

        // Script calistirma izinleri de denetim izi dekoratoru ile sarilir:
        // izin vermek, sunucuda kod calistirma yetkisi vermektir.
        services.Replace(ServiceDescriptor.Singleton<ISkillScriptGrantStore, AuditingSkillScriptGrantStore>(
            static provider => new AuditingSkillScriptGrantStore(
                ActivatorUtilities.CreateInstance<SqlSkillScriptGrantStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<ILogger<AuditingSkillScriptGrantStore>>())));
        services.Replace(ServiceDescriptor.Singleton<IRunStore, SqlRunStore>());

        // Workflow tanimlari ve kontrol noktalari (Faz 15). Tanim deposu denetim
        // izi dekoratoruyle sarilir; kontrol noktasi deposu sarilmaz: nokta bir
        // kullanici karari degil, yurutmenin yan urunudur ve her super-step'te
        // yazilir - denetim izini doldururdu.
        services.Replace(ServiceDescriptor.Singleton<IWorkflowDefinitionStore, AuditingWorkflowDefinitionStore>(
            static provider => new AuditingWorkflowDefinitionStore(
                ActivatorUtilities.CreateInstance<SqlWorkflowDefinitionStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<ILogger<AuditingWorkflowDefinitionStore>>())));
        services.Replace(
            ServiceDescriptor.Singleton<IWorkflowCheckpointStore, SqlWorkflowCheckpointStore>());

        // Is kuyrugu ve zamanlama depolari (Faz 17). Ikisi de sarilmaz: kuyruk
        // kendi durum makinesini (Pending/Leased/Running/...) tasir, workflow
        // kontrol noktasi deposu ile ayni gerekce.
        services.Replace(ServiceDescriptor.Singleton<IJobStore, SqlJobStore>());
        services.Replace(ServiceDescriptor.Singleton<IJobScheduleStore, SqlJobScheduleStore>());

        // Degerlendirme (eval) takim/vaka/kosu deposu (Faz 18). Sarilmaz: is
        // kuyrugu depolariyla ayni gerekce, kendi durum makinesini tasir.
        services.Replace(ServiceDescriptor.Singleton<IEvalStore, SqlEvalStore>());

        // Kota ve webhook depolari (Faz 21).
        //
        // 🚨 Cok ornekli bir dagitimda kota icin bu depo ZORUNLUDUR: bellek ici
        // sayac her surecte ayridir ve kota, ornek sayisina bolunur.
        //
        // Ikisi de denetim izi dekoratoruyle SARILMAZ. Gerekce ayridir:
        // kota kurallari ve abonelikler yonetici kararidir (sarilmayi hak
        // eder), ama tuketim sayaci ve teslim gecmisi yurutmenin yan urunudur
        // ve her calistirmada yazilir — denetim izini gurultuye bogardi. Ikisi
        // ayni sozlesmede yasadigi icin sarmalamak "ya hep ya hic"tir; yonetici
        // eylemleri HTTP katmaninda ayrica denetim izine yazilir.
        services.Replace(ServiceDescriptor.Singleton<IQuotaStore, SqlQuotaStore>());
        services.Replace(ServiceDescriptor.Singleton<IWebhookStore, SqlWebhookStore>());

        // A/B deneyleri (Faz 19). IAgentDefinitionStore ile ayni gerekceyle
        // denetim izi dekoratoruyle sarilir: Admin'in bilincli bir karari,
        // yurutmenin yan urunu degil.
        services.Replace(ServiceDescriptor.Singleton<IExperimentStore, AuditingExperimentStore>(
            static provider => new AuditingExperimentStore(
                ActivatorUtilities.CreateInstance<SqlExperimentStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<ITenantContext>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuditingExperimentStore>>())));

        services.Replace(ServiceDescriptor.Singleton<ISessionStore, AuditingSessionStore>(
            static provider => new AuditingSessionStore(
                ActivatorUtilities.CreateInstance<SqlSessionStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<ITenantContext>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<ILogger<AuditingSessionStore>>())));
        services.Replace(ServiceDescriptor.Singleton<ITraceStore, SqlTraceStore>());
        services.Replace(ServiceDescriptor.Singleton<IToolApprovalRuleStore, AuditingToolApprovalRuleStore>(
            static provider => new AuditingToolApprovalRuleStore(
                ActivatorUtilities.CreateInstance<SqlToolApprovalRuleStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<ILogger<AuditingToolApprovalRuleStore>>())));
        services.Replace(ServiceDescriptor.Singleton<IMcpServerStore, AuditingMcpServerStore>(
            static provider => new AuditingMcpServerStore(
                ActivatorUtilities.CreateInstance<SqlMcpServerStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<ILogger<AuditingMcpServerStore>>())));
        services.Replace(ServiceDescriptor.Singleton<ITenantStore, AuditingTenantStore>(
            static provider => new AuditingTenantStore(
                ActivatorUtilities.CreateInstance<SqlTenantStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<ITenantContext>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<ILogger<AuditingTenantStore>>())));

        // Sohbet gecmisi. AgentDefinitionCompiler bunu derledigi her agent'a baglar;
        // kayitli degilse MAF'in bellek ici varsayilani kullanilir.
        services.Replace(ServiceDescriptor.Singleton<ChatHistoryProvider, SqlChatHistoryProvider>());

        // Ekler. IAttachmentStorage kayitliysa (S3/Blob) icerik orada yasar; bu
        // depo yalnizca ustveriyi tutar.
        services.Replace(ServiceDescriptor.Singleton<IAttachmentStore>(
            static provider => ActivatorUtilities.CreateInstance<SqlAttachmentStore>(provider)));

        // Kalici agent dosya belleği (Faz 14, 14.5): FileMemoryProvider ve
        // TextSearchProvider kod degismeden buraya doner (K-110).
#pragma warning disable MAAI001 // AgentFileStore — gerekce AgentPrismServiceCollectionExtensions'daki ile ayni.
        services.Replace(ServiceDescriptor.Singleton<AgentFileStore>(
            static provider => ActivatorUtilities.CreateInstance<SqlAgentFileStore>(provider)));
#pragma warning restore MAAI001

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
