using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>AgentPrism'i bagimlilik enjeksiyonuna kaydeden uzantilar.</summary>
public static class AgentPrismServiceCollectionExtensions
{
    /// <summary>
    /// AgentPrism'i barindirici olusturucusuna ekler ve yapilandirmayi
    /// <c>AgentPrism</c> bolumunden okur.
    /// </summary>
    /// <param name="builder">Barindirici olusturucusu.</param>
    /// <returns>Yapilandirma zinciri.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> <see langword="null"/> ise.</exception>
    public static IAgentPrismBuilder AddAgentPrism(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Services.AddAgentPrism(builder.Configuration.GetSection(AgentPrismOptions.SectionName));
    }

    /// <summary>AgentPrism'i servis koleksiyonuna ekler.</summary>
    /// <param name="services">Servis koleksiyonu.</param>
    /// <param name="configurationSection">Ayarlarin okunacagi yapilandirma bolumu.</param>
    /// <returns>Yapilandirma zinciri.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> <see langword="null"/> ise.</exception>
    /// <remarks>
    /// <para>
    /// Tum servisler <c>TryAdd</c> ile kaydedilir. Kendi uygulamanizi bu cagridan
    /// <em>once</em> kaydederseniz sizinki kazanir; AgentPrism uzerine yazmaz.
    /// </para>
    /// <para>
    /// Hicbir ek yapilandirma yapilmazsa AgentPrism bellek ici depolarla calisir
    /// ve veritabani gerektirmez.
    /// </para>
    /// </remarks>
    public static IAgentPrismBuilder AddAgentPrism(
        this IServiceCollection services,
        IConfiguration? configurationSection = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<AgentPrismOptions>().ValidateOnStart();

        if (configurationSection is not null)
        {
            // Yapilandirma ELLE baglanir. `optionsBuilder.Bind(section)` yansimaya
            // dayanir ve IL2026 + IL3050 uretir; kaynak ureteci bunu build sirasinda
            // gizler ama `dotnet format` analyzer gecisinde tanilar yeniden ortaya
            // cikar. Elle baglama her iki kapida da temizdir ve bir paket
            // bagimliligini (Options.ConfigurationExtensions) ortadan kaldirir.
            // Gerekce: docs/KARARLAR.md, karar K-021.
            services.Configure<AgentPrismOptions>(options => Bind(configurationSection, options));
        }

        // Toplu ve zamanlanmis calistirma (Faz 17). Ayri bir bolum: PostgreSql
        // paketinin AgentPrismPostgreSqlOptions'i gibi kendi SectionName'ini
        // tasir, ama AgentPrismOptions'in aksine ayri bir Use...() cagrisi
        // olmadan da her zaman kayitlidir (K-018 — depolar birinci sinif).
        services.AddOptions<AgentPrismSchedulingOptions>().ValidateOnStart();

        if (configurationSection is not null)
        {
            services.Configure<AgentPrismSchedulingOptions>(
                options => BindScheduling(configurationSection.GetSection("Scheduling"), options));
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<AgentPrismSchedulingOptions>, AgentPrismSchedulingOptionsValidator>());

        // Tek yurutucu secimi (Faz 42). Ayni gerekce: kendi SectionName'ini
        // tasir, ayri bir Use...() cagrisi gerektirmez. Varsayilan Enabled=false;
        // kapaliyken InMemorySingletonLeaseStore'a hicbir cagri gitmez (K1).
        services.AddOptions<SingletonExecutionOptions>().ValidateOnStart();

        if (configurationSection is not null)
        {
            services.Configure<SingletonExecutionOptions>(
                options => BindSingletonExecution(configurationSection.GetSection("SingletonExecution"), options));
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<SingletonExecutionOptions>, SingletonExecutionOptionsValidator>());
        services.TryAddSingleton<ISingletonLeaseStore, InMemorySingletonLeaseStore>();

        // Kota ve olay yayini (Faz 21). Zamanlama ile ayni gerekce: kendi
        // SectionName'ini tasir ve ayri bir Use...() cagrisi gerektirmez.
        services.AddOptions<AgentPrismQuotaOptions>().ValidateOnStart();
        services.AddOptions<AgentPrismWebhookOptions>().ValidateOnStart();
        services.AddOptions<AgentPrismRateLimitOptions>().ValidateOnStart();

        // Veri saklama ve arsivleme (Faz 25). Ayni gerekce: kendi SectionName'ini
        // tasir, ayri bir Use...() cagrisi gerektirmez.
        services.AddOptions<AgentPrismRetentionOptions>().ValidateOnStart();

        // Idempotency-Key destegi (Faz 43). Ayni gerekce: kendi SectionName'ini
        // tasir, ayri bir Use...() cagrisi gerektirmez.
        services.AddOptions<AgentPrismIdempotencyOptions>().ValidateOnStart();

        // Kuyruga alinan (dayanikli) calistirma (Faz 46). Ayni gerekce: kendi
        // SectionName'ini tasir, ayri bir Use...() cagrisi gerektirmez.
        services.AddOptions<AgentPrismAsyncRunOptions>().ValidateOnStart();

        // Icerik denetimi (Faz 48). Ayarlar her zaman kayitlidir ama hicbir
        // IContentGuard kayitli DEGILSE hic okunmazlar: denetim sarmalayicisi boru
        // hattina eklenmez. K1'in kapisi bir bayrak degil, kaydin kendisidir.
        services.AddOptions<AgentPrismContentGuardOptions>().ValidateOnStart();
        services.AddOptions<PatternContentGuardOptions>().ValidateOnStart();

        // Cevrimici degerlendirme (Faz 49). Ayni gerekce: kendi SectionName'ini
        // tasir, ayri bir Use...() cagrisi gerektirmez. Iki kapili varsayilan
        // (Enabled=false VE SampleRate=0) yargic modelinin HIC cagrilmamasini
        // saglar — bkz. OnlineEvaluationOptions sinif belgesi.
        services.AddOptions<OnlineEvaluationOptions>().ValidateOnStart();

        if (configurationSection is not null)
        {
            services.Configure<AgentPrismQuotaOptions>(
                options => BindQuotas(configurationSection.GetSection("Quotas"), options));
            services.Configure<AgentPrismWebhookOptions>(
                options => BindWebhooks(configurationSection.GetSection("Webhooks"), options));
            services.Configure<AgentPrismRateLimitOptions>(
                options => BindRateLimit(configurationSection.GetSection("RateLimit"), options));
            services.Configure<AgentPrismRetentionOptions>(
                options => BindRetention(configurationSection.GetSection("Retention"), options));
            services.Configure<AgentPrismIdempotencyOptions>(
                options => BindIdempotency(configurationSection.GetSection("Idempotency"), options));
            services.Configure<AgentPrismAsyncRunOptions>(
                options => BindAsyncRun(configurationSection.GetSection("AsyncRun"), options));
            services.Configure<OnlineEvaluationOptions>(
                options => BindOnlineEvaluation(configurationSection.GetSection("OnlineEvaluation"), options));

            var contentGuardSection = configurationSection.GetSection("ContentGuard");

            services.Configure<AgentPrismContentGuardOptions>(
                options => BindContentGuard(contentGuardSection, options));

            var patternSection = contentGuardSection.GetSection("Pattern");

            services.Configure<PatternContentGuardOptions>(
                options => BindPatternContentGuard(patternSection, options));

            // 🚨 Yerlesik guard yalnizca bolum GERCEKTEN varsa kaydedilir. Kayit
            // K1'in kapisidir: kayit yoksa denetim sarmalayicisi boru hattina hic
            // eklenmez ve maliyet tam olarak sifir kalir. Kod tarafindan acmanin
            // yolu AddPatternContentGuard() cagrisidir.
            if (patternSection.Exists())
            {
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IContentGuard, PatternContentGuard>());
            }
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<AgentPrismQuotaOptions>, AgentPrismQuotaOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<AgentPrismWebhookOptions>, AgentPrismWebhookOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<AgentPrismRetentionOptions>, AgentPrismRetentionOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<OnlineEvaluationOptions>, OnlineEvaluationOptionsValidator>());

        services.AddLogging();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<AgentPrismOptions>, AgentPrismOptionsValidator>());

        // Kiraci baglami. Cok kiracili kurulumda tuketici kendi uygulamasini
        // bu cagridan once kaydeder.
        services.TryAddSingleton<ITenantContext, SingleTenantContext>();

        // Defterler.
        services.TryAddSingleton<IToolRegistry, ToolRegistry>();

        // Devre kesici IModelProviderRegistry'den ONCE kaydedilir: ModelProviderRegistry
        // onu kurucusunda cozer ve ureteceği her IChatClient'i onunla sarar.
        // Acik fabrika kullaniliyor: yerlesik DI kabi varsayilan deger tasiyan kurucu
        // parametrelerini doldurmaz, TimeProvider kayitli olmayabilir.
        services.TryAddSingleton(static provider => new ModelProviderCircuitBreaker(
            provider.GetRequiredService<IOptionsMonitor<AgentPrismOptions>>(),
            provider.GetService<TimeProvider>()));

        // Icerik denetimi boru hatti (Faz 48). Her zaman kayitlidir ama HasGuards
        // false ise defter denetim sarmalayicisini hic eklemez. Acik fabrika: kayit
        // sirasi onemsizdir, IContentGuard'lar bu cagridan sonra da eklenebilir
        // (GetServices lazy cozer).
        services.TryAddSingleton(static provider => new ContentGuardPipeline(
            provider.GetServices<IContentGuard>(),
            provider.GetRequiredService<IOptionsMonitor<AgentPrismContentGuardOptions>>(),
            provider.GetRequiredService<IAuditLog>(),
            provider.GetRequiredService<IAuditActorResolver>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<ILoggerFactory>()));

        // Defter boru hattinin TAMAMINI kurar (Faz 48'de saglayici paketlerinden
        // tasindi). Acik fabrika zorunludur: yerlesik DI kabi varsayilan deger
        // tasiyan kurucu parametrelerini doldurmaz.
        services.TryAddSingleton<IModelProviderRegistry>(static provider => new ModelProviderRegistry(
            provider.GetServices<IModelProvider>(),
            provider.GetService<ModelProviderCircuitBreaker>(),
            provider.GetService<IAttachmentStore>(),
            provider.GetService<ITenantContext>(),
            provider.GetService<ContentGuardPipeline>(),
            provider.GetService<ILoggerFactory>()));

        // Maliyet cozumleyici (Faz 20): model kataloğu, sonra AgentPrism:Pricing.
        services.TryAddSingleton<IRunPricingResolver, RunPricingResolver>();
        services.TryAddSingleton<RunCostRecalculationService>();

        // Hata siniflandirici (Faz 44). Taksonomi AgentPrism'in gorusudur;
        // TryAddSingleton sayesinde tuketicinin kendi siniflandiricisi kazanir (K4).
        services.TryAddSingleton<IRunErrorClassifier, DefaultRunErrorClassifier>();

        // Calistirma iptali defteri (Faz 32). Her zaman kayitlidir: bellek ici
        // bir sozluk tutmaktan baska bir yan etkisi yoktur (K-165'in "yeni
        // davranis varsayilan kapali gelir" karari acik bir yan etki
        // ureten ozellikler icindir, bu defter degildir).
        services.TryAddSingleton<IRunCancellationRegistry, RunCancellationRegistry>();

        // Saglik onbellegi ve isteğe bagli arka plan tazeleyici. Acik fabrika: ayni
        // gerekce, TimeProvider kayitli olmayabilir.
        services.TryAddSingleton(static provider => new ModelProviderHealthCache(
            provider.GetServices<IModelProvider>(),
            provider.GetRequiredService<IOptionsMonitor<AgentPrismOptions>>(),
            provider.GetService<ModelProviderCircuitBreaker>(),
            provider.GetService<TimeProvider>()));
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, ModelProviderHealthBackgroundService>());

        // Teshis toplayicisi (Faz 33). IAgentCatalog ve IToolRegistry bu noktadan
        // sonra kayit edilir ama acik fabrika lazy cozer; kayit sirasi onemli degildir.
        services.TryAddSingleton(static provider => new AgentPrismDiagnosticsCollector(
            provider.GetServices<IModelProvider>(),
            provider.GetRequiredService<ModelProviderHealthCache>(),
            provider.GetServices<ISqlPersistenceDiagnostics>(),
            provider.GetServices<SqlPersistenceRegistrationMarker>(),
            provider.GetRequiredService<IAgentCatalog>(),
            provider.GetRequiredService<IToolRegistry>(),
            provider.GetService<ModelProviderCircuitBreaker>()));

        // Sohbet gecmisi saglayicisi. Kayitli olmasaydi MAF her agent icin kendi
        // bellek ici saglayicisini kurardi ve o ornege disaridan erisilemezdi;
        // /api/sessions/{id} gecmisi yalnizca PostgreSQL acikken okunabilirdi.
        // Acik kayit iki modda da ayni okuma yolunu verir. Durum oturumun
        // StateBag'inde yasar, saglayicinin alanlarinda degil; bu yuzden tek
        // ornegin tum oturumlarca paylasilmasi MAF'in ongordugu kullanimdir.
        // AgentPrism.PostgreSql bunu PostgresChatHistoryProvider ile degistirir.
        services.TryAddSingleton<Microsoft.Agents.AI.ChatHistoryProvider>(
            static _ => new Microsoft.Agents.AI.InMemoryChatHistoryProvider(
                new Microsoft.Agents.AI.InMemoryChatHistoryProviderOptions()));

        // Bellek destekli AIContextProvider'lar (FileMemoryProvider,
        // TextSearchProvider) icin dosya deposu. MAF'in kendi soyutlamasi
        // (dosya sistemi degil); bu fazda kalici surumu yok, bellek icinde
        // yasar. Kalici surum Faz 14+'in depolama tablosuna baglanacak.
        // MAAI001: AgentFileStore "evaluation purposes only" — gerekce
        // AgentDefinitionCompiler'daki ile aynidir.
#pragma warning disable MAAI001
        services.TryAddSingleton<Microsoft.Agents.AI.AgentFileStore>(
            static _ => new Microsoft.Agents.AI.InMemoryAgentFileStore());
#pragma warning restore MAAI001

        // Derleyici ve onbellek.
        services.TryAddSingleton<CompiledAgentCache>();

        // Alt agent cozucusu. Katalogu KURUCUSUNDA degil ilk kullanimda ister;
        // aksi halde IAgentCatalog -> IAgentSource -> AgentDefinitionCompiler ->
        // cozucu -> IAgentCatalog dairesi kurulamazdi.
        services.TryAddSingleton<CallableAgentResolver>();

#pragma warning disable MAAI001 // AgentFileStore — gerekce AgentDefinitionCompiler'daki ile aynidir.
        services.TryAddSingleton(static provider => new AgentDefinitionCompiler(
            provider.GetRequiredService<IModelProviderRegistry>(),
            provider.GetRequiredService<IToolRegistry>(),
            provider.GetService<Microsoft.Extensions.Logging.ILoggerFactory>(),
            provider,
            // Kayitli degilse MAF'in bellek ici varsayilani kullanilir.
            // AgentPrism.PostgreSql bunu PostgresChatHistoryProvider ile doldurur.
            provider.GetService<Microsoft.Agents.AI.ChatHistoryProvider>(),
            provider.GetRequiredService<AgentSkillCatalog>(),
            // Kayitli degilse script destegi yoktur: hicbir script calistirilamaz.
            provider.GetService<SkillScriptSupport>(),
            provider.GetRequiredService<CallableAgentResolver>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<IOptions<AgentPrismOptions>>().Value.UtilityModel,
            provider.GetRequiredService<Microsoft.Agents.AI.AgentFileStore>(),
            // Kayitli degilse McpResourceUris kullanan bir tanim derleme hatasi alir.
            // AgentPrism.Mcp'nin UseMcp() cagrisi bunu kaydeder.
            provider.GetService<IMcpResourceContextProviderFactory>()));
#pragma warning restore MAAI001

        // Tanim dogrulama ucu (Faz 34, F-60). Gercek derleme yolunu kendi
        // sirasiyla tekrar eder; IAgentCatalog burada dogrudan alinabilir
        // cunku dogrulayici (CallableAgentResolver'in aksine) IAgentCatalog'un
        // KENDI kurulumunun bir parcasi degil, ona sonradan eklenen bir
        // tuketicidir — dairesel bagimlilik riski yoktur.
        services.TryAddSingleton(static provider => new AgentDefinitionValidator(
            provider.GetRequiredService<IModelProviderRegistry>(),
            provider.GetRequiredService<IToolRegistry>(),
            provider.GetRequiredService<AgentSkillCatalog>(),
            provider.GetRequiredService<IAgentCatalog>(),
            provider.GetRequiredService<AgentDefinitionCompiler>(),
            provider.GetRequiredService<IOptions<AgentPrismOptions>>(),
            // Kayitli degilse AgentPrism.Mcp kullanilmiyordur; eksik tool adlari
            // taze bir MCP taramasi denenmeden dogrudan hata olarak raporlanir.
            provider.GetService<IMcpToolRefresher>()));

        // Denetim izi. Aktor AuditActorContext'ten (AsyncLocal) okunur;
        // AgentPrism.AspNetCore her korumali istegin basinda oraya HttpContext.User'i
        // yazar. Boylece Core, ASP.NET Core'a bagimlilik eklemeden aktoru okuyabilir.
        services.TryAddSingleton<IAuditLog, InMemoryAuditLog>();
        services.TryAddSingleton<IAuditActorResolver, AmbientAuditActorResolver>();

        // Bellek ici depolar, denetim izi yazan dekoratorlerle sarilmis olarak
        // kaydedilir. Kalicilik paketi (AgentPrism.PostgreSql) ayni dekoratorlerle
        // kendi uygulamalarini sarar (bkz. UsePostgreSql); boylece denetim izi
        // hangi depo kayitli olursa olsun ayni sekilde calisir.
        // Gerekce: docs/09-YONETISIM-VE-DENETIM-IZI.md, bolum 9.2.
        services.TryAddSingleton<IAgentDefinitionStore>(static provider => new AuditingAgentDefinitionStore(
            new InMemoryAgentDefinitionStore(provider.GetRequiredService<ITenantContext>()),
            provider.GetRequiredService<IAuditLog>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<IAuditActorResolver>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuditingAgentDefinitionStore>>()));
        services.TryAddSingleton<IAgentSkillStore, InMemoryAgentSkillStore>();
        services.TryAddSingleton(static provider => new AgentSkillCatalog(
            provider.GetServices<CodeSkillRegistration>(),
            provider.GetRequiredService<IAgentSkillStore>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<IOptions<AgentPrismOptions>>()));
        // Calistirma/mesaj puanlari (Faz 31). IRunStore'dan ONCE kaydedilir:
        // InMemoryRunStore.GetStatisticsAsync ozet hesabinda bu paylasilan
        // tekil orneği DI uzerinden alir (bkz. InMemoryRunStore kurucusu).
        services.TryAddSingleton<IRunScoreStore, InMemoryRunScoreStore>();
        services.TryAddSingleton<IRunStore>(static provider => new InMemoryRunStore(
            provider.GetRequiredService<IRunScoreStore>(),
            provider.GetRequiredService<ITenantContext>()));

        // Calistirma girdileri (Faz 47). Depo her zaman kayitlidir (K-018:
        // birinci sinif) — yeniden oynatma bir SQL saglayicisi olmadan da
        // calisir. Kalici saglayicilar bunu kendi uygulamalariyla degistirir.
        services.TryAddSingleton<IRunInputStore, InMemoryRunInputStore>();

        // Script calistirma izinleri. Depo her zaman kayitlidir; calistirma
        // ozelligi ise UseSkillScripts cagrilana kadar KAPALIDIR. Izin kaydinin
        // varligi tek basina bir sey calistirmaz.
        services.TryAddSingleton<ISkillScriptGrantStore>(static provider => new AuditingSkillScriptGrantStore(
            new InMemorySkillScriptGrantStore(),
            provider.GetRequiredService<IAuditLog>(),
            provider.GetRequiredService<IAuditActorResolver>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuditingSkillScriptGrantStore>>()));

        services.TryAddSingleton<ISessionStore>(static provider => new AuditingSessionStore(
            new InMemorySessionStore(provider.GetRequiredService<ITenantContext>()),
            provider.GetRequiredService<IAuditLog>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<IAuditActorResolver>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuditingSessionStore>>()));
        services.TryAddSingleton<ITraceStore>(static provider => new InMemoryTraceStore(
            provider.GetRequiredService<ITenantContext>()));
        services.TryAddSingleton<IToolApprovalRuleStore>(static provider => new AuditingToolApprovalRuleStore(
            new InMemoryToolApprovalRuleStore(),
            provider.GetRequiredService<IAuditLog>(),
            provider.GetRequiredService<IAuditActorResolver>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuditingToolApprovalRuleStore>>()));
        services.TryAddSingleton<IMcpServerStore>(static provider => new AuditingMcpServerStore(
            new InMemoryMcpServerStore(),
            provider.GetRequiredService<IAuditLog>(),
            provider.GetRequiredService<IAuditActorResolver>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuditingMcpServerStore>>()));
        // Ek deposu ve tur denetleyicisi. IAttachmentStorage kayitli degilse icerik
        // dogrudan bellekte (uretimde: veritabaninda) yasar.
        services.TryAddSingleton<AttachmentTypeGuard>();
        services.TryAddSingleton<IAttachmentStore>(
            static provider => new InMemoryAttachmentStore(provider.GetService<IAttachmentStorage>()));

        // Workflow tanimlari ve kontrol noktalari. Depolar HER ZAMAN kayitlidir;
        // yurutme motoru ise UseWorkflows() cagrilana kadar KAPALIDIR. Bu ayrim
        // bilinclidir: HTTP katmani tanimlari motor olmadan da listeleyip
        // yonetebilmelidir, yalnizca "calistir" ucu 501 doner.
        services.TryAddSingleton<IWorkflowDefinitionStore>(static provider => new AuditingWorkflowDefinitionStore(
            new InMemoryWorkflowDefinitionStore(),
            provider.GetRequiredService<IAuditLog>(),
            provider.GetRequiredService<IAuditActorResolver>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuditingWorkflowDefinitionStore>>()));
        services.TryAddSingleton<IWorkflowCheckpointStore, InMemoryWorkflowCheckpointStore>();

        // Is kuyrugu ve zamanlama depolari (Faz 17). Workflow depolariyla ayni
        // ayrim: depolar HER ZAMAN kayitlidir; arka plan iscisi ise
        // AgentPrismSchedulingOptions.RunWorker ile acilir/kapanir. Bu ikisi
        // dekoratorle sarilmaz — is kuyrugu kendi durum makinesini tasir ve
        // denetim izi buraya Faz 18'de eklenebilir.
        services.TryAddSingleton<IJobStore, InMemoryJobStore>();
        services.TryAddSingleton<IJobScheduleStore, InMemoryJobScheduleStore>();

        // Uc varsayilan isleyici: agent toplu calistirma, kuyruga alinan tekil
        // calistirma (Faz 46) ve workflow. Ucu de TryAddEnumerable ile eklenir;
        // Faz 18 (eval) kendi isleyicisini AddJobHandler<T>() ile ayni sekilde
        // ekler.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHandler, AgentBatchJobHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHandler, AgentRunJobHandler>());

        // Acik fabrika kullaniliyor: yerlesik DI kabi varsayilan deger tasiyan
        // kurucu parametrelerini doldurmaz ve IWorkflowRunner cogu kurulumda
        // kayitli degildir (UseWorkflows() cagrilmadikca) — ayni gerekce
        // RunRecordingAgentDecorator kaydinda da gecerlidir.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHandler, WorkflowJobHandler>(
            static provider => new WorkflowJobHandler(
                provider.GetService<IWorkflowRunner>(),
                provider.GetService<Microsoft.Extensions.Logging.ILogger<WorkflowJobHandler>>())));

        // Degerlendirme (eval) altyapisi (Faz 18). Takim/vaka/kosu deposu her
        // zaman kayitlidir; kosular ayni is kuyrugu uzerinden (JobKind.Eval)
        // yurutulur. Denetim defteri ozel (AddEvalCheck ile eklenen) kayitlardan
        // kurulur; yerlesik alti tur EvalCheckRegistry icinde sabittir.
        services.TryAddSingleton<IEvalStore, InMemoryEvalStore>();
        services.TryAddSingleton<EvalCheckRegistry>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHandler, EvalJobHandler>());

        // Uretimden vaka terfisi (Faz 45, F-53). Sorgu metnini calistirmanin
        // oturumundan okur; bagimliliklarin tumu yukarida zaten kayitlidir.
        services.TryAddSingleton<RunToCasePromoter>();

        // Cevrimici degerlendirme (Faz 49). Ornekleyici ve is isleyicisi her
        // zaman kayitlidir (K-018: birinci sinif); hicbir sey PUANLAMAZ cunku
        // OnlineEvaluationOptions varsayilani iki kapiyi de kapali tutar
        // (Enabled=false, SampleRate=0) ve kayitli hicbir IRunJudge yoktur —
        // yerlesik yargici acmanin yolu AddModelRunJudge() cagrisidir.
        services.TryAddSingleton<RunSampler>();
        services.TryAddSingleton<OnlineEvalSummaryService>();

        // 🚨 Concrete tip AYRICA kaydedilir: POST /api/runs/{id}/judge ucu
        // JudgeRunAsync'i dogrudan cagirmak icin OnlineEvalJobHandler'i KENDI
        // tipiyle ister. TryAddEnumerable(Singleton<IJobHandler, T>) yalnizca
        // arayuz uzerinden cozulebilen bir kayit uretir; concrete tipi AYRI
        // kaydetmezsek uc DI'da bulamaz. Ayni factory AYNI ornegi doner, boylece
        // iki kayit (concrete + arayuz) tek bir singleton'i paylasir.
        services.TryAddSingleton<OnlineEvalJobHandler>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHandler, OnlineEvalJobHandler>(
            static provider => provider.GetRequiredService<OnlineEvalJobHandler>()));

        // Kota ve olay yayini (Faz 21). Depolar her zaman kayitlidir; kural
        // tanimlanmadikca hicbir sey reddedilmez, abone yoksa hicbir olay
        // yayilmaz. Bu yuzden ayri bir Use...() cagrisi gerekmez.
        services.TryAddSingleton<IQuotaStore, InMemoryQuotaStore>();
        services.TryAddSingleton<IWebhookStore, InMemoryWebhookStore>();

        // 🚨 SSRF korumasi bu istemcinin icine gomulüdur; tuketici degistiremez
        // (K-164). Acik fabrika: TimeProvider kayitli olmayabilir.
        services.TryAddSingleton(static provider => new WebhookHttpClient(
            provider.GetRequiredService<IOptionsMonitor<AgentPrismWebhookOptions>>()));

        services.TryAddSingleton<IWebhookPublisher>(static provider => new WebhookPublisher(
            provider.GetRequiredService<IWebhookStore>(),
            provider.GetRequiredService<IJobStore>(),
            provider.GetRequiredService<IOptionsMonitor<AgentPrismWebhookOptions>>(),
            provider.GetService<TimeProvider>(),
            provider.GetService<Microsoft.Extensions.Logging.ILogger<WebhookPublisher>>()));

        services.TryAddSingleton(static provider => new QuotaEnforcer(
            provider.GetRequiredService<IQuotaStore>(),
            provider.GetRequiredService<IOptionsMonitor<AgentPrismQuotaOptions>>(),
            provider.GetService<IWebhookPublisher>(),
            provider.GetService<TimeProvider>(),
            provider.GetService<Microsoft.Extensions.Logging.ILogger<QuotaEnforcer>>()));

        // Kota olceri (Faz 35). IHostedService olarak eklenmesinin tek amaci
        // konteynerin bu nesneyi barindirici baslarken ERKEN cozmesidir; aksi
        // halde hicbir tuketici cozmedigi surece ObservableGauge'lar hic
        // olusmaz. EnableQuotaUsageGauge kapaliyken (varsayilan) olcer yine de
        // olusur ama onbellege hic dokunmaz — bkz. sinif belgesi.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, QuotaUsageObserver>(
            static provider => new QuotaUsageObserver(
                provider.GetRequiredService<IQuotaStore>(),
                provider.GetRequiredService<ITenantStore>(),
                provider.GetRequiredService<IOptionsMonitor<AgentPrismObservabilityOptions>>(),
                provider.GetRequiredService<IOptionsMonitor<AgentPrismQuotaOptions>>(),
                provider.GetService<System.Diagnostics.Metrics.IMeterFactory>(),
                provider.GetService<TimeProvider>(),
                provider.GetService<Microsoft.Extensions.Logging.ILogger<QuotaUsageObserver>>())));

        // Teslim isleyicisi Faz 17'nin AYNI kuyrugunu kullanir (K-160).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHandler, WebhookDeliveryJobHandler>(
            static provider => new WebhookDeliveryJobHandler(
                provider.GetRequiredService<IWebhookStore>(),
                provider.GetRequiredService<WebhookHttpClient>(),
                provider.GetRequiredService<IOptionsMonitor<AgentPrismWebhookOptions>>(),
                provider.GetService<IConfiguration>(),
                provider.GetService<TimeProvider>(),
                provider.GetService<Microsoft.Extensions.Logging.ILogger<WebhookDeliveryJobHandler>>())));

        // Idempotency-Key destegi (Faz 43). Depo her zaman kayitlidir (K-018:
        // birinci sinif); tek ornekli dagitimda InMemoryIdempotencyStore
        // yeterlidir. Ayri bir Use...() cagrisi gerekmez.
        services.TryAddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();

        // Veri saklama ve arsivleme (Faz 25). Politika/kosu deposu her zaman
        // kayitlidir (kontrol duzlemi); veri duzlemi (IRetentionStore) ise bellek
        // ici kurulumda islevsizdir (NullRetentionStore) — saklama yalniz kalici
        // bir SQL saglayicisi acikken anlamlidir. IArchiveSink kayitli DEGILSE
        // arsivleme isteyen bir politika hicbir satir silmez (Faz 25 karari).
        services.TryAddSingleton<IRetentionPolicyStore, InMemoryRetentionPolicyStore>();
        services.TryAddSingleton<IRetentionStore, NullRetentionStore>();
        services.TryAddSingleton<RetentionPolicyResolver>();

        // Acik fabrika kullaniliyor: yerlesik DI kabi varsayilan deger tasiyan
        // kurucu parametrelerini doldurmaz (IArchiveSink, TimeProvider, ILogger
        // kayitli olmayabilir).
        services.TryAddSingleton(static provider => new RetentionExecutor(
            provider.GetRequiredService<IRetentionPolicyStore>(),
            provider.GetRequiredService<IRetentionStore>(),
            provider.GetRequiredService<RetentionPolicyResolver>(),
            provider.GetRequiredService<IOptionsMonitor<AgentPrismRetentionOptions>>(),
            provider.GetService<IArchiveSink>(),
            provider.GetService<TimeProvider>(),
            provider.GetService<Microsoft.Extensions.Logging.ILogger<RetentionExecutor>>()));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHandler, RetentionJobHandler>(
            static provider => new RetentionJobHandler(provider.GetRequiredService<RetentionExecutor>())));

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, JobWorkerBackgroundService>());

        // A/B deneyleri (Faz 19). Admin'in olusturdugu/baslattigi/durdurdugu bir
        // varlik oldugu icin IAgentDefinitionStore ile ayni gerekceyle denetim
        // izi dekoratoruyle sarilir (IEvalStore/IJobStore'un aksine, onlar
        // yurutmenin yan urunudur).
        services.TryAddSingleton<IExperimentStore>(static provider => new AuditingExperimentStore(
            new InMemoryExperimentStore(),
            provider.GetRequiredService<IAuditLog>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<IAuditActorResolver>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuditingExperimentStore>>()));
        services.TryAddSingleton<ExperimentAssignmentResolver>();

        services.TryAddSingleton<ITenantStore>(static provider => new AuditingTenantStore(
            new InMemoryTenantStore(),
            provider.GetRequiredService<IAuditLog>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<IAuditActorResolver>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuditingTenantStore>>()));

        // Telemetri. Metrikler IMeterFactory kayitliysa onun uzerinden kurulur;
        // degilse kendi Meter'ini olusturur — tuketici AddMetrics() cagirmaya
        // zorlanmaz.
        services.TryAddSingleton(static provider => new AgentPrismMetrics(
            provider.GetService<System.Diagnostics.Metrics.IMeterFactory>()));

        // Span toplayici ActivityListener'i kurucusunda kaydeder; bu yuzden
        // ilk cozulmesi yeterlidir. RunRecordingAgentDecorator onu cozer.
        services.TryAddSingleton<RunTraceCollector>();

        // Onay kurallarini degerlendiren servis.
        services.TryAddSingleton<ToolApprovalRuleEvaluator>();

        // Oturum yasam dongusu. Depodan bagimsizdir.
        // Acik fabrika kullaniliyor: yerlesik DI kabi varsayilan deger tasiyan
        // kurucu parametrelerini doldurmaz, TimeProvider kayitli olmayabilir.
        services.TryAddSingleton(static provider => new AgentSessionManager(
            provider.GetRequiredService<ISessionStore>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetService<TimeProvider>()));

        // Konusma dallandirma (Faz 47). IConversationBranchStore yalnizca bir SQL
        // saglayicisi acikken kayitlidir; kayitsizken servis "desteklenmiyor"
        // der ve uc 501 doner (bkz. ConversationBranchService.IsSupported).
        services.TryAddSingleton(static provider => new ConversationBranchService(
            provider.GetRequiredService<ISessionStore>(),
            provider.GetRequiredService<IAgentCatalog>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetService<IConversationBranchStore>(),
            provider.GetService<TimeProvider>()));

        // Yeniden oynatma (Faz 47). Katalogu DEGIL, tanim deposunu ve derleyiciyi
        // kullanir: model bindirmesi ve tool modlari tanimi yeniden derlemeyi
        // gerektirir ve sonuc onbellege GIRMEZ.
        services.TryAddSingleton(static provider => new RunReplayService(
            provider.GetRequiredService<IRunStore>(),
            provider.GetRequiredService<IRunInputStore>(),
            provider.GetRequiredService<IAgentDefinitionStore>(),
            provider.GetRequiredService<IAgentCatalog>(),
            provider.GetRequiredService<AgentDefinitionCompiler>(),
            provider.GetRequiredService<IToolRegistry>(),
            provider.GetServices<IAgentDecorator>(),
            provider.GetRequiredService<ITenantContext>()));

        // Katalog kaynaklari. TryAddEnumerable ayni tipin iki kez eklenmesini engeller.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentSource, CodeAgentSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentSource, DefinitionStoreAgentSource>());

        // Sarmalayicilar. Uygulama sirasi Order ile belirlenir:
        // calistirma kaydi (0) → telemetri (10) → tool onayi (20) → agent.
        //
        // Kayit dekoratoru acik fabrika ile kuruluyor: yerlesik DI kabi varsayilan
        // deger tasiyan kurucu parametrelerini doldurmaz ve TimeProvider kayitli
        // olmayabilir.
        //
        // Iki tur argumanli asiri yukleme SART: tek argumanli bicimde fabrikanin
        // donus tipi IAgentDecorator olur ve TryAddEnumerable uygulamayi ayirt
        // edemeyip "indistinguishable from other services" hatasi verir.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentDecorator, RunRecordingAgentDecorator>(
            static provider => new RunRecordingAgentDecorator(
                provider.GetRequiredService<IRunStore>(),
                provider.GetRequiredService<ITenantContext>(),
                provider.GetRequiredService<IOptions<AgentPrismOptions>>(),
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RunRecordingAgent>>(),
                provider.GetRequiredService<AgentPrismMetrics>(),
                provider.GetRequiredService<RunTraceCollector>(),
                provider.GetService<TimeProvider>(),
                provider.GetRequiredService<IRunPricingResolver>(),
                // 🚨 Bu iki satir olmadan kurucu parametreleri null kalir ve kota
                // sayaci hic artmaz, hicbir run.* olayi yayilmaz — derleme ve
                // testler yesil gorunurdu (K-157'nin dersi).
                provider.GetRequiredService<QuotaEnforcer>(),
                provider.GetRequiredService<IWebhookPublisher>(),
                provider.GetRequiredService<IRunCancellationRegistry>(),
                provider.GetRequiredService<IRunErrorClassifier>(),
                provider.GetRequiredService<IRunInputStore>(),
                // Faz 49: kayitli olmasi tek basina hicbir sey orneklemez, bkz.
                // RunSampler/OnlineEvaluationOptions sinif belgeleri.
                provider.GetRequiredService<RunSampler>())));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentDecorator, OpenTelemetryAgentDecorator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentDecorator, ToolApprovalAgentDecorator>());

        services.TryAddSingleton<IAgentCatalog, CompositeAgentCatalog>();

        return new AgentPrismBuilder(services);
    }

    /// <summary>
    /// Bir <see cref="IJobHandler"/> genisleme noktasi ekler.
    /// </summary>
    /// <typeparam name="THandler">Eklenecek isleyici tipi.</typeparam>
    /// <param name="services">Servis koleksiyonu.</param>
    /// <returns>Zincirleme icin ayni koleksiyon.</returns>
    /// <remarks>
    /// Faz 18 (eval) kendi isleyicisini bu metotla ekler. <c>TryAddEnumerable</c>
    /// kullanilir: ayni tip iki kez eklenirse yalnizca ilki sayilir.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> <see langword="null"/> ise.</exception>
    public static IServiceCollection AddJobHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(
        this IServiceCollection services)
        where THandler : class, IJobHandler
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHandler, THandler>());

        return services;
    }

    /// <summary>
    /// Toplu ve zamanlanmis calistirma (Faz 17) ayarlarini kod ile ayarlar.
    /// </summary>
    /// <param name="services">Servis koleksiyonu.</param>
    /// <param name="configure">Ayar degistirici. Verilmezse yalnizca varsayilanlar/yapilandirma gecerli olur.</param>
    /// <returns>Zincirleme icin ayni koleksiyon.</returns>
    /// <remarks>
    /// Is kuyrugu ve zamanlama depolari <c>AddAgentPrism()</c> ile zaten
    /// kayitlidir; bu metot yalnizca ayarlari degistirir (ornegin
    /// <c>o.RunWorker = false</c> ile bu surecteki arka plan iscisini
    /// kapatmak). <c>PostConfigure</c> kullanilir, boylece kod ile verilen
    /// deger yapilandirma dosyasindan gelen degerden her zaman kazanir.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> <see langword="null"/> ise.</exception>
    public static IServiceCollection UseScheduling(
        this IServiceCollection services,
        Action<AgentPrismSchedulingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.PostConfigure(configure);
        }

        return services;
    }

    /// <summary>
    /// Yapilandirma bolumunu ayar nesnesine elle baglar.
    /// </summary>
    /// <remarks>
    /// Yeni bir ayar eklendiginde bu metoda da eklenmelidir. Karsiliginda
    /// AgentPrism.Core yansimasiz ve AOT uyumlu kalir.
    /// </remarks>
    private static void Bind(IConfiguration section, AgentPrismOptions options)
    {
        if (section[nameof(AgentPrismOptions.DefaultTenantId)] is { Length: > 0 } tenantId)
        {
            options.DefaultTenantId = tenantId;
        }

        // Her alt bolum KENDI varliginidan sorumludur. Erken donus, ilk bolum
        // tanimli degilse sonrakilerin hic okunmamasina yol acar; olculdu:
        // RunRecording yazilmamis bir yapilandirmada Observability sessizce
        // yok sayiliyordu.
        BindRunRecording(section.GetSection(nameof(AgentPrismOptions.RunRecording)), options.RunRecording);
        BindObservability(section.GetSection(nameof(AgentPrismOptions.Observability)), options.Observability);
        BindCircuitBreaker(section.GetSection(nameof(AgentPrismOptions.CircuitBreaker)), options.CircuitBreaker);
        BindHealth(section.GetSection(nameof(AgentPrismOptions.Health)), options.Health);
        BindAudit(section.GetSection(nameof(AgentPrismOptions.Audit)), options.Audit);
        BindSkills(section.GetSection(nameof(AgentPrismOptions.Skills)), options.Skills);
        BindAgentGraph(section.GetSection(nameof(AgentPrismOptions.AgentGraph)), options.AgentGraph);
        BindAttachments(section.GetSection(nameof(AgentPrismOptions.Attachments)), options.Attachments);
        BindPricing(section.GetSection(nameof(AgentPrismOptions.Pricing)), options.Pricing);
        options.UtilityModel = BindUtilityModel(section.GetSection(nameof(AgentPrismOptions.UtilityModel)));
    }

    /// <summary>
    /// <c>AgentPrism:Pricing</c> bolumunu baglar. <c>Currency</c> ve <c>Voice</c>
    /// anahtarlari rezervedir; diger her cocuk bir saglayici adi olarak okunur.
    /// </summary>
    private static void BindPricing(IConfigurationSection section, AgentPrismPricingOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (section[nameof(AgentPrismPricingOptions.Currency)] is { Length: > 0 } currency)
        {
            options.Currency = currency;
        }

        BindVoicePricing(section.GetSection(nameof(AgentPrismPricingOptions.Voice)), options);

        foreach (var providerSection in section.GetChildren())
        {
            // Rezerve anahtarlar. Bolum elle baglandigi icin bu liste TEK
            // dogruluk noktasidir; unutulan bir anahtar saglayici adi sanilir.
            if (string.Equals(
                    providerSection.Key,
                    nameof(AgentPrismPricingOptions.Currency),
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    providerSection.Key,
                    nameof(AgentPrismPricingOptions.Voice),
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var models = new Dictionary<string, ModelPriceOverride>(StringComparer.OrdinalIgnoreCase);

            foreach (var modelSection in providerSection.GetChildren())
            {
                var input = ReadDecimal(modelSection, "Input");
                var output = ReadDecimal(modelSection, "Output");

                if (input is null && output is null)
                {
                    continue;
                }

                models[modelSection.Key] = new ModelPriceOverride
                {
                    InputCostPerMillionTokens = input,
                    OutputCostPerMillionTokens = output,
                };
            }

            if (models.Count > 0)
            {
                options.Providers[providerSection.Key] = models;
            }
        }
    }

    /// <summary>
    /// <c>AgentPrism:Pricing:Voice</c> bolumunu baglar.
    /// </summary>
    /// <remarks>
    /// Ses ucretlendirmesi token degil karakter (uretim) veya sure (cozum)
    /// bazlidir; bu yuzden ayri bir sozluge yazilir ve token fiyatlariyla
    /// toplanmaz.
    /// </remarks>
    private static void BindVoicePricing(IConfigurationSection section, AgentPrismPricingOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        foreach (var providerSection in section.GetChildren())
        {
            var models = new Dictionary<string, VoicePriceOverride>(StringComparer.OrdinalIgnoreCase);

            foreach (var modelSection in providerSection.GetChildren())
            {
                var perMillionCharacters = ReadDecimal(
                    modelSection,
                    nameof(VoicePriceOverride.PerMillionCharacters));

                var perMinute = ReadDecimal(modelSection, nameof(VoicePriceOverride.PerMinute));

                if (perMillionCharacters is null && perMinute is null)
                {
                    continue;
                }

                models[modelSection.Key] = new VoicePriceOverride
                {
                    PerMillionCharacters = perMillionCharacters,
                    PerMinute = perMinute,
                };
            }

            if (models.Count > 0)
            {
                options.Voice[providerSection.Key] = models;
            }
        }
    }

    private static decimal? ReadDecimal(IConfiguration section, string key)
        => decimal.TryParse(section[key], NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>
    /// Yardimci model baglantisini yapilandirmadan okur.
    /// </summary>
    /// <remarks>
    /// <see cref="ModelBinding.Provider"/> ve <see cref="ModelBinding.Model"/>
    /// zorunludur; ikisi de dolu degilse hicbir baglanti kurulmaz. Kismen
    /// doldurulmus bir baglanti, yanlislikla eksik yazilmis bir yapilandirmayi
    /// sessizce kabul etmis olur.
    /// </remarks>
    private static ModelBinding? BindUtilityModel(IConfigurationSection section)
    {
        if (!section.Exists())
        {
            return null;
        }

        var provider = section[nameof(ModelBinding.Provider)];
        var model = section[nameof(ModelBinding.Model)];

        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        float.TryParse(section[nameof(ModelBinding.Temperature)], NumberStyles.Float, CultureInfo.InvariantCulture, out var temperature);
        int.TryParse(section[nameof(ModelBinding.MaxOutputTokens)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxOutputTokens);
        float.TryParse(section[nameof(ModelBinding.TopP)], NumberStyles.Float, CultureInfo.InvariantCulture, out var topP);

        return new ModelBinding
        {
            Provider = provider,
            Model = model,
            Temperature = section[nameof(ModelBinding.Temperature)] is { Length: > 0 } ? temperature : null,
            MaxOutputTokens = section[nameof(ModelBinding.MaxOutputTokens)] is { Length: > 0 } ? maxOutputTokens : null,
            TopP = section[nameof(ModelBinding.TopP)] is { Length: > 0 } ? topP : null,
            ReasoningEffort = section[nameof(ModelBinding.ReasoningEffort)],
        };
    }

    private static void BindAgentGraph(IConfigurationSection section, AgentPrismAgentGraphOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (int.TryParse(
                section[nameof(AgentPrismAgentGraphOptions.MaxDepth)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxDepth))
        {
            options.MaxDepth = maxDepth;
        }

        if (long.TryParse(
                section[nameof(AgentPrismAgentGraphOptions.MaxTotalTokens)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxTokens))
        {
            options.MaxTotalTokens = maxTokens;
        }

        if (int.TryParse(
                section[nameof(AgentPrismAgentGraphOptions.MaxTotalRuns)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxRuns))
        {
            options.MaxTotalRuns = maxRuns;
        }
    }

    private static void BindAttachments(IConfigurationSection section, AgentPrismAttachmentOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (long.TryParse(
                section[nameof(AgentPrismAttachmentOptions.MaxBytes)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxBytes))
        {
            options.MaxBytes = maxBytes;
        }

        var allowed = section.GetSection(nameof(AgentPrismAttachmentOptions.AllowedMediaTypes))
            .GetChildren()
            .Select(static child => child.Value)
            .Where(static value => value is { Length: > 0 })
            .ToArray();

        if (allowed.Length > 0)
        {
            options.AllowedMediaTypes.Clear();

            foreach (var mediaType in allowed)
            {
                options.AllowedMediaTypes.Add(mediaType!);
            }
        }
    }

    private static void BindAudit(IConfigurationSection section, AgentPrismAuditOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (section[nameof(AgentPrismAuditOptions.ActorClaimType)] is { Length: > 0 } claimType)
        {
            options.ActorClaimType = claimType;
        }
    }

    private static void BindSkills(IConfigurationSection section, AgentPrismSkillOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (int.TryParse(section[nameof(AgentPrismSkillOptions.MaxSkillsPerAgent)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxSkills))
        {
            options.MaxSkillsPerAgent = maxSkills;
        }

        if (int.TryParse(section[nameof(AgentPrismSkillOptions.MaxInstructionsLength)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxInstructions))
        {
            options.MaxInstructionsLength = maxInstructions;
        }

        if (int.TryParse(section[nameof(AgentPrismSkillOptions.MaxResourceContentLength)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxResourceContent))
        {
            options.MaxResourceContentLength = maxResourceContent;
        }

        if (int.TryParse(section[nameof(AgentPrismSkillOptions.MaxResourcesPerSkill)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxResources))
        {
            options.MaxResourcesPerSkill = maxResources;
        }

        BindSkillScripts(section.GetSection(nameof(AgentPrismSkillOptions.Scripts)), options.Scripts);
    }

    /// <summary>Script calistirma ayarlarini yapilandirmadan baglar.</summary>
    /// <remarks>
    /// <c>Enabled</c> ve <c>PlatformIsolationAcknowledged</c> bilerek buradan da
    /// okunabilir: bir kurulum, ayni imaji farkli ortamlarda script destegi acik
    /// veya kapali calistirabilmelidir. Dogrulama yine de her ikisini birlikte
    /// arar; yalnizca <c>Enabled</c> acmak acilista hata verir.
    /// </remarks>
    private static void BindSkillScripts(IConfigurationSection section, AgentPrismSkillScriptOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismSkillScriptOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (TryReadBool(section, nameof(AgentPrismSkillScriptOptions.PlatformIsolationAcknowledged), out var acknowledged))
        {
            options.PlatformIsolationAcknowledged = acknowledged;
        }

        if (TryReadBool(section, nameof(AgentPrismSkillScriptOptions.AllowStoredScripts), out var allowStored))
        {
            options.AllowStoredScripts = allowStored;
        }

        if (TimeSpan.TryParse(section[nameof(AgentPrismSkillScriptOptions.Timeout)], CultureInfo.InvariantCulture, out var timeout))
        {
            options.Timeout = timeout;
        }

        if (int.TryParse(section[nameof(AgentPrismSkillScriptOptions.MaxOutputBytes)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxOutput))
        {
            options.MaxOutputBytes = maxOutput;
        }

        if (int.TryParse(section[nameof(AgentPrismSkillScriptOptions.MaxArgumentBytes)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxArguments))
        {
            options.MaxArgumentBytes = maxArguments;
        }

        if (int.TryParse(section[nameof(AgentPrismSkillScriptOptions.MaxScriptContentLength)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxContent))
        {
            options.MaxScriptContentLength = maxContent;
        }

        if (int.TryParse(section[nameof(AgentPrismSkillScriptOptions.MaxScriptsPerSkill)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxScripts))
        {
            options.MaxScriptsPerSkill = maxScripts;
        }

        if (int.TryParse(section[nameof(AgentPrismSkillScriptOptions.MaxConcurrentPerTenant)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var perTenant))
        {
            options.MaxConcurrentPerTenant = perTenant;
        }

        if (int.TryParse(section[nameof(AgentPrismSkillScriptOptions.MaxConcurrentTotal)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var total))
        {
            options.MaxConcurrentTotal = total;
        }

        if (int.TryParse(section[nameof(AgentPrismSkillScriptOptions.SearchDepth)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var depth))
        {
            options.SearchDepth = depth;
        }

        BindList(section.GetSection(nameof(AgentPrismSkillScriptOptions.SkillRoots)), options.SkillRoots);
        BindList(section.GetSection(nameof(AgentPrismSkillScriptOptions.EnvironmentAllowList)), options.EnvironmentAllowList);

        foreach (var child in section.GetSection(nameof(AgentPrismSkillScriptOptions.Interpreters)).GetChildren())
        {
            if (child.Value is { Length: > 0 } interpreter)
            {
                options.Interpreters[child.Key] = interpreter;
            }
        }
    }

    /// <summary>Yapilandirma dizisini var olan bir listeye yazar.</summary>
    /// <remarks>
    /// Liste yapilandirmada tanimliysa varsayilan icerik <strong>tamamen</strong>
    /// degistirilir. Birlestirme yapilsaydi, ortam degiskeni beyaz listesini
    /// daraltmak imkansiz olurdu.
    /// </remarks>
    private static void BindList(IConfigurationSection section, IList<string> target)
    {
        if (!section.Exists())
        {
            return;
        }

        var values = section.GetChildren()
            .Select(static child => child.Value)
            .Where(static value => value is { Length: > 0 })
            .ToArray();

        if (values.Length == 0)
        {
            return;
        }

        target.Clear();

        foreach (var value in values)
        {
            target.Add(value!);
        }
    }

    private static void BindCircuitBreaker(IConfigurationSection section, AgentPrismCircuitBreakerOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismCircuitBreakerOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (int.TryParse(
                section[nameof(AgentPrismCircuitBreakerOptions.FailureThreshold)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var failureThreshold))
        {
            options.FailureThreshold = failureThreshold;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismCircuitBreakerOptions.BreakDuration)],
                CultureInfo.InvariantCulture,
                out var breakDuration))
        {
            options.BreakDuration = breakDuration;
        }
    }

    /// <summary>Zamanlama ayarlarini yapilandirmadan baglar (Faz 17).</summary>
    private static void BindScheduling(IConfigurationSection section, AgentPrismSchedulingOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismSchedulingOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (TryReadBool(section, nameof(AgentPrismSchedulingOptions.RunWorker), out var runWorker))
        {
            options.RunWorker = runWorker;
        }

        if (int.TryParse(
                section[nameof(AgentPrismSchedulingOptions.MaxConcurrentJobs)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxConcurrentJobs))
        {
            options.MaxConcurrentJobs = maxConcurrentJobs;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismSchedulingOptions.PollInterval)],
                CultureInfo.InvariantCulture,
                out var pollInterval))
        {
            options.PollInterval = pollInterval;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismSchedulingOptions.LeaseDuration)],
                CultureInfo.InvariantCulture,
                out var leaseDuration))
        {
            options.LeaseDuration = leaseDuration;
        }

        if (int.TryParse(
                section[nameof(AgentPrismSchedulingOptions.MaxAttempts)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxAttempts))
        {
            options.MaxAttempts = maxAttempts;
        }

        if (int.TryParse(
                section[nameof(AgentPrismSchedulingOptions.MaxItemsPerJob)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxItemsPerJob))
        {
            options.MaxItemsPerJob = maxItemsPerJob;
        }
    }

    /// <summary>Tek yurutucu secimi ayarlarini yapilandirmadan baglar (Faz 42).</summary>
    private static void BindSingletonExecution(IConfigurationSection section, SingletonExecutionOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(SingletonExecutionOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (TimeSpan.TryParse(
                section[nameof(SingletonExecutionOptions.LeaseDuration)],
                CultureInfo.InvariantCulture,
                out var leaseDuration))
        {
            options.LeaseDuration = leaseDuration;
        }

        if (section[nameof(SingletonExecutionOptions.OwnerId)] is { Length: > 0 } ownerId)
        {
            options.OwnerId = ownerId;
        }
    }

    /// <summary><c>AgentPrism:Quotas</c> bolumunu baglar.</summary>
    private static void BindQuotas(IConfigurationSection section, AgentPrismQuotaOptions options)
    {
        // Her alt bolum KENDI varliğindan sorumludur: erken donus sonraki
        // bolumleri sessizce yutar (bkz. docs/hafiza/cekirdek-calistirma.md).
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismQuotaOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (section[nameof(AgentPrismQuotaOptions.TimeZone)] is { Length: > 0 } timeZone)
        {
            options.TimeZone = timeZone;
        }

        if (TryReadBool(section, nameof(AgentPrismQuotaOptions.AllowOnStoreFailure), out var allowOnFailure))
        {
            options.AllowOnStoreFailure = allowOnFailure;
        }

        var thresholds = section.GetSection(nameof(AgentPrismQuotaOptions.ThresholdPercents));

        if (thresholds.Exists())
        {
            var parsed = thresholds.GetChildren()
                .Select(child => int.TryParse(child.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent) ? percent : -1)
                .Where(percent => percent > 0)
                .ToList();

            if (parsed.Count > 0)
            {
                options.ThresholdPercents.Clear();

                foreach (var percent in parsed)
                {
                    options.ThresholdPercents.Add(percent);
                }
            }
        }
    }

    /// <summary><c>AgentPrism:RateLimit</c> bolumunu baglar.</summary>
    private static void BindRateLimit(IConfigurationSection section, AgentPrismRateLimitOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismRateLimitOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (int.TryParse(
                section[nameof(AgentPrismRateLimitOptions.PermitLimit)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var permitLimit))
        {
            options.PermitLimit = permitLimit;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismRateLimitOptions.Window)],
                CultureInfo.InvariantCulture,
                out var window))
        {
            options.Window = window;
        }

        if (int.TryParse(
                section[nameof(AgentPrismRateLimitOptions.QueueLimit)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var queueLimit))
        {
            options.QueueLimit = queueLimit;
        }

        if (Enum.TryParse<RateLimitPartitionKind>(
                section[nameof(AgentPrismRateLimitOptions.Partition)],
                ignoreCase: true,
                out var partition))
        {
            options.Partition = partition;
        }
    }

    /// <summary><c>AgentPrism:Idempotency</c> bolumunu baglar.</summary>
    private static void BindIdempotency(IConfigurationSection section, AgentPrismIdempotencyOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismIdempotencyOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (int.TryParse(
                section[nameof(AgentPrismIdempotencyOptions.MaxKeyLength)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxKeyLength))
        {
            options.MaxKeyLength = maxKeyLength;
        }
    }

    /// <summary><c>AgentPrism:AsyncRun</c> bolumunu baglar.</summary>
    private static void BindAsyncRun(IConfigurationSection section, AgentPrismAsyncRunOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismAsyncRunOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (int.TryParse(
                section[nameof(AgentPrismAsyncRunOptions.MaxAttempts)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxAttempts))
        {
            options.MaxAttempts = maxAttempts;
        }
    }

    /// <summary><c>AgentPrism:ContentGuard</c> bolumunu baglar.</summary>
    private static void BindContentGuard(IConfigurationSection section, AgentPrismContentGuardOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismContentGuardOptions.InspectInput), out var inspectInput))
        {
            options.InspectInput = inspectInput;
        }

        if (TryReadBool(section, nameof(AgentPrismContentGuardOptions.InspectOutput), out var inspectOutput))
        {
            options.InspectOutput = inspectOutput;
        }

        if (TryReadBool(section, nameof(AgentPrismContentGuardOptions.BufferStreamingOutput), out var buffer))
        {
            options.BufferStreamingOutput = buffer;
        }
    }

    /// <summary><c>AgentPrism:ContentGuard:Pattern</c> bolumunu baglar.</summary>
    /// <remarks>
    /// <see cref="PiiPatterns"/> bir <c>[Flags]</c> enum'udur ve yapilandirmada
    /// virgulle ayrilmis ad listesi olarak yazilir (ornek:
    /// <c>"Email,CreditCard"</c>). <c>Enum.TryParse</c> AOT temizdir (olculdu,
    /// <c>docs/hafiza/build-ve-analyzer.md</c>).
    /// </remarks>
    private static void BindPatternContentGuard(IConfigurationSection section, PatternContentGuardOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        BindList(section.GetSection(nameof(PatternContentGuardOptions.DeniedTerms)), options.DeniedTerms);

        if (Enum.TryParse<PiiPatterns>(section[nameof(PatternContentGuardOptions.MaskedPii)], ignoreCase: true, out var pii))
        {
            options.MaskedPii = pii;
        }

        if (section[nameof(PatternContentGuardOptions.MaskReplacement)] is { Length: > 0 } replacement)
        {
            options.MaskReplacement = replacement;
        }
    }

    /// <summary><c>AgentPrism:Webhooks</c> bolumunu baglar.</summary>
    private static void BindWebhooks(IConfigurationSection section, AgentPrismWebhookOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismWebhookOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (TryReadBool(section, nameof(AgentPrismWebhookOptions.AllowPrivateNetworkTargets), out var allowPrivate))
        {
            options.AllowPrivateNetworkTargets = allowPrivate;
        }

        if (TryReadBool(section, nameof(AgentPrismWebhookOptions.AllowInsecureHttp), out var allowHttp))
        {
            options.AllowInsecureHttp = allowHttp;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismWebhookOptions.Timeout)],
                CultureInfo.InvariantCulture,
                out var timeout))
        {
            options.Timeout = timeout;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismWebhookOptions.SignatureTolerance)],
                CultureInfo.InvariantCulture,
                out var tolerance))
        {
            options.SignatureTolerance = tolerance;
        }

        if (int.TryParse(
                section[nameof(AgentPrismWebhookOptions.MaxResponseBytes)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxResponseBytes))
        {
            options.MaxResponseBytes = maxResponseBytes;
        }

        if (int.TryParse(
                section[nameof(AgentPrismWebhookOptions.DisableAfterConsecutiveFailures)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var disableAfter))
        {
            options.DisableAfterConsecutiveFailures = disableAfter;
        }

        var delays = section.GetSection(nameof(AgentPrismWebhookOptions.RetryDelays));

        if (delays.Exists())
        {
            var parsed = delays.GetChildren()
                .Select(child => TimeSpan.TryParse(child.Value, CultureInfo.InvariantCulture, out var delay) ? delay : TimeSpan.Zero)
                .Where(delay => delay > TimeSpan.Zero)
                .ToList();

            if (parsed.Count > 0)
            {
                options.RetryDelays.Clear();

                foreach (var delay in parsed)
                {
                    options.RetryDelays.Add(delay);
                }
            }
        }
    }

    /// <summary><c>AgentPrism:Retention</c> bolumunu baglar.</summary>
    private static void BindRetention(IConfigurationSection section, AgentPrismRetentionOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismRetentionOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (int.TryParse(
                section[nameof(AgentPrismRetentionOptions.BatchSize)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var batchSize))
        {
            options.BatchSize = batchSize;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismRetentionOptions.BatchDelay)],
                CultureInfo.InvariantCulture,
                out var batchDelay))
        {
            options.BatchDelay = batchDelay;
        }

        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.RunEvents)), options.RunEvents);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.ToolInvocations)), options.ToolInvocations);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.Spans)), options.Spans);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.Jobs)), options.Jobs);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.WebhookDeliveries)), options.WebhookDeliveries);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.EvalCaseResults)), options.EvalCaseResults);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.WorkflowCheckpoints)), options.WorkflowCheckpoints);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.SkillScriptGrants)), options.SkillScriptGrants);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.Attachments)), options.Attachments);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.Sessions)), options.Sessions);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.Conversations)), options.Conversations);
    }

    private static void BindRetentionTarget(IConfigurationSection section, RetentionTargetOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (int.TryParse(
                section[nameof(RetentionTargetOptions.MaxAgeDays)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxAgeDays))
        {
            options.MaxAgeDays = maxAgeDays;
        }

        if (TryReadBool(section, nameof(RetentionTargetOptions.Archive), out var archive))
        {
            options.Archive = archive;
        }
    }

    private static void BindHealth(IConfigurationSection section, AgentPrismHealthOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismHealthOptions.CacheTtl)],
                CultureInfo.InvariantCulture,
                out var cacheTtl))
        {
            options.CacheTtl = cacheTtl;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismHealthOptions.BackgroundInterval)],
                CultureInfo.InvariantCulture,
                out var backgroundInterval))
        {
            options.BackgroundInterval = backgroundInterval;
        }
    }

    private static void BindRunRecording(IConfigurationSection recording, AgentPrismRunRecordingOptions options)
    {
        if (!recording.Exists())
        {
            return;
        }

        if (TryReadBool(recording, nameof(AgentPrismRunRecordingOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (TryReadBool(recording, nameof(AgentPrismRunRecordingOptions.RecordMessageDeltas), out var recordDeltas))
        {
            options.RecordMessageDeltas = recordDeltas;
        }

        if (TryReadBool(recording, nameof(AgentPrismRunRecordingOptions.RecordToolPayloads), out var recordPayloads))
        {
            options.RecordToolPayloads = recordPayloads;
        }

        if (int.TryParse(
                recording[nameof(AgentPrismRunRecordingOptions.MaxPayloadLength)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxPayloadLength))
        {
            options.MaxPayloadLength = maxPayloadLength;
        }
    }

    private static void BindObservability(IConfigurationSection section, AgentPrismObservabilityOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismObservabilityOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (TryReadBool(section, nameof(AgentPrismObservabilityOptions.PersistSpans), out var persistSpans))
        {
            options.PersistSpans = persistSpans;
        }

        if (TryReadBool(
                section,
                nameof(AgentPrismObservabilityOptions.AlwaysPersistFailures),
                out var alwaysPersistFailures))
        {
            options.AlwaysPersistFailures = alwaysPersistFailures;
        }

        if (TryReadBool(
                section,
                nameof(AgentPrismObservabilityOptions.RecordSensitiveData),
                out var recordSensitiveData))
        {
            options.RecordSensitiveData = recordSensitiveData;
        }

        if (double.TryParse(
                section[nameof(AgentPrismObservabilityOptions.SuccessSampleRatio)],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var sampleRatio))
        {
            options.SuccessSampleRatio = sampleRatio;
        }

        if (int.TryParse(
                section[nameof(AgentPrismObservabilityOptions.MaxSpansPerRun)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxSpans))
        {
            options.MaxSpansPerRun = maxSpans;
        }

        if (TryReadBool(
                section,
                nameof(AgentPrismObservabilityOptions.EnableQuotaUsageGauge),
                out var enableQuotaUsageGauge))
        {
            options.EnableQuotaUsageGauge = enableQuotaUsageGauge;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismObservabilityOptions.QuotaUsageRefreshInterval)],
                CultureInfo.InvariantCulture,
                out var quotaUsageRefreshInterval))
        {
            options.QuotaUsageRefreshInterval = quotaUsageRefreshInterval;
        }
    }

    /// <summary><c>AgentPrism:OnlineEvaluation</c> bolumunu baglar.</summary>
    private static void BindOnlineEvaluation(IConfigurationSection section, OnlineEvaluationOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(OnlineEvaluationOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (double.TryParse(
                section[nameof(OnlineEvaluationOptions.SampleRate)],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var sampleRate))
        {
            options.SampleRate = sampleRate;
        }

        if (int.TryParse(
                section[nameof(OnlineEvaluationOptions.MaxScoresPerHour)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxScoresPerHour))
        {
            options.MaxScoresPerHour = maxScoresPerHour;
        }

        var agentNames = section.GetSection(nameof(OnlineEvaluationOptions.AgentNames));

        if (agentNames.Exists())
        {
            var parsed = agentNames.GetChildren()
                .Select(static child => child.Value)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            if (parsed.Count > 0)
            {
                options.AgentNames.Clear();

                foreach (var name in parsed)
                {
                    options.AgentNames.Add(name!);
                }
            }
        }

        if (int.TryParse(
                section[nameof(OnlineEvaluationOptions.LowScoreThreshold)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var lowScoreThreshold))
        {
            options.LowScoreThreshold = lowScoreThreshold;
        }

        if (int.TryParse(
                section[nameof(OnlineEvaluationOptions.MinSampleSize)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var minSampleSize))
        {
            options.MinSampleSize = minSampleSize;
        }

        if (TimeSpan.TryParse(
                section[nameof(OnlineEvaluationOptions.EvaluationWindow)],
                CultureInfo.InvariantCulture,
                out var evaluationWindow))
        {
            options.EvaluationWindow = evaluationWindow;
        }
    }

    private static bool TryReadBool(IConfiguration section, string key, out bool value)
        => bool.TryParse(section[key], out value);
}
