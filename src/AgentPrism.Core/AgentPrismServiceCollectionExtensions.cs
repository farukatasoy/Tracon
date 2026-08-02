using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
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
        services.TryAddSingleton<IModelProviderRegistry, ModelProviderRegistry>();

        // Saglik onbellegi ve isteğe bagli arka plan tazeleyici. Acik fabrika: ayni
        // gerekce, TimeProvider kayitli olmayabilir.
        services.TryAddSingleton(static provider => new ModelProviderHealthCache(
            provider.GetServices<IModelProvider>(),
            provider.GetRequiredService<IOptionsMonitor<AgentPrismOptions>>(),
            provider.GetService<ModelProviderCircuitBreaker>(),
            provider.GetService<TimeProvider>()));
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, ModelProviderHealthBackgroundService>());

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

        // Derleyici ve onbellek.
        services.TryAddSingleton<CompiledAgentCache>();
        services.TryAddSingleton(static provider => new AgentDefinitionCompiler(
            provider.GetRequiredService<IModelProviderRegistry>(),
            provider.GetRequiredService<IToolRegistry>(),
            provider.GetService<Microsoft.Extensions.Logging.ILoggerFactory>(),
            provider,
            // Kayitli degilse MAF'in bellek ici varsayilani kullanilir.
            // AgentPrism.PostgreSql bunu PostgresChatHistoryProvider ile doldurur.
            provider.GetService<Microsoft.Agents.AI.ChatHistoryProvider>()));

        // Bellek ici depolar. Kalicilik paketi (AgentPrism.PostgreSql) bunlari
        // kendi uygulamalariyla degistirir.
        services.TryAddSingleton<IAgentDefinitionStore, InMemoryAgentDefinitionStore>();
        services.TryAddSingleton<IRunStore, InMemoryRunStore>();
        services.TryAddSingleton<ISessionStore, InMemorySessionStore>();
        services.TryAddSingleton<ITraceStore, InMemoryTraceStore>();
        services.TryAddSingleton<IToolApprovalRuleStore, InMemoryToolApprovalRuleStore>();
        services.TryAddSingleton<IMcpServerStore, InMemoryMcpServerStore>();
        services.TryAddSingleton<ITenantStore, InMemoryTenantStore>();

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
                provider.GetService<TimeProvider>())));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentDecorator, OpenTelemetryAgentDecorator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentDecorator, ToolApprovalAgentDecorator>());

        services.TryAddSingleton<IAgentCatalog, CompositeAgentCatalog>();

        return new AgentPrismBuilder(services);
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
    }

    private static bool TryReadBool(IConfiguration section, string key, out bool value)
        => bool.TryParse(section[key], out value);
}
