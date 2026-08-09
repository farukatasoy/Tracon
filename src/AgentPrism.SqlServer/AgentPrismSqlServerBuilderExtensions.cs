using System.Globalization;
using Microsoft.Agents.AI;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>AgentPrism zincirine SQL Server kaliciligini ekleyen uzantilar.</summary>
public static class AgentPrismSqlServerBuilderExtensions
{
    /// <summary>Baglanti dizesi vererek SQL Server kaliciligini acar.</summary>
    /// <param name="builder">AgentPrism zinciri.</param>
    /// <param name="connectionString">SQL Server baglanti dizesi.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> <see langword="null"/> ise.</exception>
    /// <exception cref="ArgumentException"><paramref name="connectionString"/> bos ise.</exception>
    public static IAgentPrismBuilder UseSqlServer(this IAgentPrismBuilder builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return builder.UseSqlServer(options => options.ConnectionString = connectionString);
    }

    /// <summary>
    /// Ayarlari <c>AgentPrism:SqlServer</c> bolumunden okuyarak SQL Server kaliciligini acar.
    /// </summary>
    /// <param name="builder">AgentPrism zinciri.</param>
    /// <param name="configurationSection">
    /// Ayarlarin okunacagi bolum. Genellikle
    /// <c>configuration.GetSection(AgentPrismSqlServerOptions.SectionName)</c>.
    /// </param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException">Parametrelerden biri <see langword="null"/> ise.</exception>
    public static IAgentPrismBuilder UseSqlServer(
        this IAgentPrismBuilder builder,
        IConfiguration configurationSection)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return builder.UseSqlServer(options => Bind(configurationSection, options));
    }

    /// <summary>Ayarlari kodda vererek SQL Server kaliciligini acar.</summary>
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
    /// Uzerine yazma burada dogrudur cunku <c>UseSqlServer()</c> tuketicinin
    /// <strong>acik</strong> tercihidir. "TryAdd ile kaydet" kurali AgentPrism'in
    /// varsayilanlari icindir, acik cagrilar icin degil.
    /// Gerekce: <c>docs/KARARLAR.md</c>, karar K-025.
    /// </para>
    /// </remarks>
    public static IAgentPrismBuilder UseSqlServer(
        this IAgentPrismBuilder builder,
        Action<AgentPrismSqlServerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var services = builder.Services;

        services.AddOptions<AgentPrismSqlServerOptions>().ValidateOnStart();
        services.Configure(configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<AgentPrismSqlServerOptions>,
            AgentPrismSqlServerOptionsValidator>());

        // Tek veri kaynagi; baglanti havuzunu SqlClient kendi yonetir.
        services.TryAddSingleton(static provider => SqlServerDataSourceFactory.Create(
            provider.GetRequiredService<IOptions<AgentPrismSqlServerOptions>>().Value));

        // Paylasilan depo katmaninin baglami. Saglayiciya ozgu her sey burada
        // toplanir; depolar Npgsql tipi gormez (Faz 23, K-176).
        // Depo kayitlariyla ayni kural: son cagri kazanir.
        services.Replace(ServiceDescriptor.Singleton(static provider =>
        {
            var options = provider.GetRequiredService<IOptions<AgentPrismSqlServerOptions>>().Value;

            return new SqlStoreContext
            {
                DataSource = provider.GetRequiredService<SqlServerDataSource>(),
                Dialect = new SqlServerDialect(options.SchemaName),
                CommandTimeoutSeconds = options.CommandTimeoutSeconds,
                AutoApplyMigrations = options.AutoApplyMigrations,
                ProviderName = "SQL Server",
            };
        }));

        // 🚨 Iki kalicilik saglayicisi ayni anda kaydedilirse son kayit kazanir.
        // Bu bir yapilandirma hatasidir; acilista uyari loglanir ve /api/diagnostics
        // bunu bildirir. Gerekce: K-183.
        services.AddSingleton(new SqlPersistenceRegistrationMarker("SQL Server"));

        services.Replace(ServiceDescriptor.Singleton(static provider => new MigrationRunner(
            provider.GetRequiredService<SqlStoreContext>(),
            provider.GetRequiredService<ILogger<MigrationRunner>>())));
        services.AddHostedService<MigrationHostedService>();

        // Teshis (Faz 33): kazanan saglayicinin MigrationRunner'i ISqlPersistenceDiagnostics
        // olarak da cozulur; ayni ornek, ek bir SQL baglantisi uretmez.
        services.Replace(ServiceDescriptor.Singleton<ISqlPersistenceDiagnostics>(
            static provider => provider.GetRequiredService<MigrationRunner>()));

        // Denetim izi defteri de bellek icinin yerini alir.
        services.Replace(ServiceDescriptor.Singleton<IAuditLog, SqlAuditLog>());

        // Bellek ici depolarin yerini alir. TryAdd burada ise yaramaz.
        //
        // Yazma yapan bes deponun tumu, Faz 9'un denetim izi dekoratorleriyle
        // sarilarak kaydedilir; boylece denetim izi bellek ici veya SQL Server
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

        // Kiraci bazli API anahtarlari (Faz 53). Sarilmaz: kota/webhook
        // depolariyla ayni gerekce, yonetici eylemleri HTTP katmaninda ayrica
        // denetim izine yazilir.
        services.Replace(ServiceDescriptor.Singleton<IApiKeyStore, SqlApiKeyStore>());

        // Veri saklama ve arsivleme (Faz 25). Ayni gerekce: politika/kosu
        // deposu sarilmaz, veri duzlemi yalniz bir SQL saglayicisi acikken
        // anlamlidir.
        services.Replace(ServiceDescriptor.Singleton<IRetentionPolicyStore, SqlRetentionPolicyStore>());
        services.Replace(ServiceDescriptor.Singleton<IRetentionStore, SqlRetentionStore>());

        // Tek yurutucu secimi (Faz 42). Bellek ici InMemorySingletonLeaseStore'un
        // yerini alir; cok ornekli bir dagitimda kira paylasimi ancak burada
        // anlamlidir.
        services.Replace(ServiceDescriptor.Singleton<ISingletonLeaseStore, SqlSingletonLeaseStore>());

        // Konusma kaydi (Faz 29). Yalniz UseVoiceConversation() cagrildiysa bir
        // sey yazar; cagrilmadiysa depo bos kalir. Denetim izi dekoratoruyle
        // SARILMAZ: kayit bir yonetici karari degil, yurutmenin yan urunudur.
        services.Replace(ServiceDescriptor.Singleton<IVoiceSessionStore, SqlVoiceSessionStore>());

        // Calistirma/mesaj puanlari (Faz 31). Denetim izi dekoratoruyle
        // SARILMAZ: kota/webhook depolariyla ayni gerekce -- bir puan
        // yonetici karari degil, kullanicidan gelen geri bildirimdir.
        services.Replace(ServiceDescriptor.Singleton<IRunScoreStore, SqlRunScoreStore>());

        // Idempotency-Key destegi (Faz 43). Bellek ici InMemoryIdempotencyStore'un
        // yerini alir; cok ornekli bir dagitimda tekillestirme ancak burada
        // anlamlidir.
        services.Replace(ServiceDescriptor.Singleton<IIdempotencyStore, SqlIdempotencyStore>());

        // Calistirma girdileri (Faz 47). Bellek ici InMemoryRunInputStore'un
        // yerini alir; yeniden oynatma ancak girdi kalicilastiginda surec
        // yeniden basladiktan sonra da calisir.
        services.Replace(ServiceDescriptor.Singleton<IRunInputStore, SqlRunInputStore>());

        // Asenkron onay kutusu (Faz 55).
        services.Replace(ServiceDescriptor.Singleton<IPendingApprovalStore, SqlPendingApprovalStore>());

        // Konusma dallandirma (Faz 47). Bellek ici karsiligi YOKTUR: MAF'in
        // InMemoryChatHistoryProvider'i gecmisi oturum durumunun opak blogunda
        // tutar ve belirli bir sira numarasina kadar kopyalanamaz. Kayit yalniz
        // burada yapilir; kayitsiz kurulumda uc 501 doner.
        services.TryAddSingleton<IConversationBranchStore, SqlConversationBranchStore>();

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
    private static void Bind(IConfiguration section, AgentPrismSqlServerOptions options)
    {
        if (section[nameof(AgentPrismSqlServerOptions.ConnectionString)] is { Length: > 0 } connectionString)
        {
            options.ConnectionString = connectionString;
        }

        if (section[nameof(AgentPrismSqlServerOptions.SchemaName)] is { Length: > 0 } schemaName)
        {
            options.SchemaName = schemaName;
        }

        if (bool.TryParse(section[nameof(AgentPrismSqlServerOptions.AutoApplyMigrations)], out var autoApply))
        {
            options.AutoApplyMigrations = autoApply;
        }

        if (int.TryParse(
                section[nameof(AgentPrismSqlServerOptions.CommandTimeoutSeconds)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var commandTimeout))
        {
            options.CommandTimeoutSeconds = commandTimeout;
        }
    }
}
