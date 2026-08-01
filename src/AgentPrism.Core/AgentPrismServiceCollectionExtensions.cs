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
        services.TryAddSingleton<IModelProviderRegistry, ModelProviderRegistry>();

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

        // Calistirma kaydi sarmalayicisi.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentDecorator, RunRecordingAgentDecorator>());

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

        var recording = section.GetSection(nameof(AgentPrismOptions.RunRecording));

        if (!recording.Exists())
        {
            return;
        }

        if (TryReadBool(recording, nameof(AgentPrismRunRecordingOptions.Enabled), out var enabled))
        {
            options.RunRecording.Enabled = enabled;
        }

        if (TryReadBool(recording, nameof(AgentPrismRunRecordingOptions.RecordMessageDeltas), out var recordDeltas))
        {
            options.RunRecording.RecordMessageDeltas = recordDeltas;
        }

        if (TryReadBool(recording, nameof(AgentPrismRunRecordingOptions.RecordToolPayloads), out var recordPayloads))
        {
            options.RunRecording.RecordToolPayloads = recordPayloads;
        }

        if (int.TryParse(
                recording[nameof(AgentPrismRunRecordingOptions.MaxPayloadLength)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxPayloadLength))
        {
            options.RunRecording.MaxPayloadLength = maxPayloadLength;
        }
    }

    private static bool TryReadBool(IConfiguration section, string key, out bool value)
        => bool.TryParse(section[key], out value);
}
