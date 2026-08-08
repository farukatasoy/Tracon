using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace AgentPrism;

/// <summary>
/// Uzak MCP sunucularindan tool kesfini kaydeden uzantilar.
/// </summary>
public static class AgentPrismMcpBuilderExtensions
{
    /// <summary>
    /// Model Context Protocol istemcisini kaydeder. Kayitli ve etkin her MCP
    /// sunucusunun tool'lari kesfedilir ve kodda kayitli tool'larin yaninda
    /// listelenir.
    /// </summary>
    /// <param name="builder">AgentPrism yapilandirma zinciri.</param>
    /// <param name="configure">Ayar degistirici.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> <see langword="null"/> ise.</exception>
    /// <remarks>
    /// <para>
    /// <strong>Guvenlik siniri.</strong> MCP sunucusu eklemek, disaridan gelen
    /// tool tanimlarini kabul etmek demektir ve tasarim kurali K2'nin
    /// ("tool'lar yalnizca kodda tanimlanir") bilincli bir istisnasidir.
    /// Su korumalarla gelir:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>Yalnizca <em>uzak</em> HTTP sunuculari; yerel surec (stdio) aktarimi yoktur.</description></item>
    ///   <item><description>MCP tool'lari varsayilan olarak <strong>onay ister</strong>.</description></item>
    ///   <item><description>Kodda kayitli bir tool'un adi MCP tarafindan ele gecirilemez.</description></item>
    ///   <item><description>Sunucu tanimi sir tasimaz; kimlik dogrulama degeri yapilandirmadan cozulur.</description></item>
    ///   <item><description>Her cagri, kaynak sunucu adiyla <c>tool_invocations</c> tablosuna yazilir.</description></item>
    /// </list>
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
    ///        .UseOpenAI(apiKey)
    ///        .UsePostgreSql(connectionString)
    ///        .UseMcp()
    ///        .UseUI();
    /// </code>
    /// </example>
    /// </remarks>
    public static IAgentPrismBuilder UseMcp(
        this IAgentPrismBuilder builder,
        Action<AgentPrismMcpOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return UseMcpCore(builder, configure);
    }

    /// <summary>
    /// Ayarlari <c>AgentPrism:Mcp</c> bolumunden okuyarak MCP istemcisini kaydeder.
    /// </summary>
    /// <param name="builder">AgentPrism yapilandirma zinciri.</param>
    /// <param name="configurationSection">
    /// Ayarlarin okunacagi bolum. Genellikle
    /// <c>configuration.GetSection(AgentPrismMcpOptions.SectionName)</c>.
    /// </param>
    /// <param name="configure">
    /// Bolum baglandiktan SONRA calisan ayar degistirici. Kodda verilen deger
    /// yapilandirmadan gelen degeri ezer.
    /// </param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> veya <paramref name="configurationSection"/>
    /// <see langword="null"/> ise.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Yapilandirma <strong>acikca</strong> verilir; AgentPrism kendiliginden
    /// <c>IConfiguration</c> okumaz. Gerekce K1 (sifir surpriz) ve depodaki
    /// diger <c>Use*</c> uzantilariyla tutarliliktir.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
    ///        .UseMcp(builder.Configuration.GetSection(AgentPrismMcpOptions.SectionName));
    /// </code>
    /// </example>
    /// </remarks>
    public static IAgentPrismBuilder UseMcp(
        this IAgentPrismBuilder builder,
        IConfiguration configurationSection,
        Action<AgentPrismMcpOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return UseMcpCore(builder, options =>
        {
            Bind(configurationSection, options);
            configure?.Invoke(options);
        });
    }

    private static IAgentPrismBuilder UseMcpCore(
        IAgentPrismBuilder builder,
        Action<AgentPrismMcpOptions>? configure)
    {
        var services = builder.Services;

        services.AddOptions<AgentPrismMcpOptions>();

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<McpOAuthTokenCacheRegistry>();
        services.TryAddSingleton<McpToolCatalog>();

        // Defter DEGISTIRILIR, TryAdd ile eklenmez: AddAgentPrism() zincirde
        // once calisir ve ToolRegistry'yi zaten kaydetmis olur. Yeni defter
        // eskisini sarmalar; kodda kayitli tool'lar oncelikli kalir.
        services.Replace(ServiceDescriptor.Singleton<IToolRegistry>(static provider => new McpToolRegistry(
            new ToolRegistry(provider.GetServices<AgentPrismToolRegistration>()),
            provider.GetRequiredService<McpToolCatalog>(),
            provider.GetRequiredService<ITenantContext>())));

        services.TryAddSingleton<IMcpToolRefresher, McpToolRefresher>();

        // Mod A: AgentDefinitionCompiler (AgentPrism.Core) bu fabrikayi
        // opsiyonel bir bagimlilik olarak cozer; kayitli degilse
        // McpResourceUris kullanan bir tanim derleme hatasi alir.
        services.TryAddSingleton<IMcpResourceContextProviderFactory, McpResourceContextProviderFactory>();

        // Prompts/Resources (22.1/22.2) ve OAuth Mod 1 (22.3): GovernanceEndpoints
        // (AgentPrism.AspNetCore) bu soyutlamalari opsiyonel servisler olarak
        // cozer; kayitli degilse ilgili uclar 501 doner.
        services.TryAddSingleton<IMcpPromptClient, McpPromptClient>();
        services.TryAddSingleton<IMcpResourceClient, McpResourceClient>();
        services.TryAddSingleton<IMcpOAuthCoordinator, McpOAuthAuthorizationCoordinator>();

        services.AddHostedService<McpDiscoveryService>();

        return builder;
    }

    /// <summary>
    /// <c>AgentPrism:Mcp</c> bolumunu elle baglar.
    /// </summary>
    /// <remarks>
    /// Elle baglama bir AOT gereksinimidir: <c>Bind()</c> yansimaya dayanir ve
    /// <c>IL2026</c> + <c>IL3050</c> uretir; kirpilmis uygulamalarda ayarlar
    /// sessizce bos kalir. Gerekce: <c>docs/KARARLAR.md</c>, karar K-021.
    /// 🚨 <see cref="AgentPrismMcpOptions"/>'a yeni bir ayar eklendiginde bu
    /// metoda da eklenmelidir; yoksa ayar sessizce baglanmaz.
    /// </remarks>
    private static void Bind(IConfiguration section, AgentPrismMcpOptions options)
    {
        if (bool.TryParse(section[nameof(AgentPrismMcpOptions.Enabled)], out var enabled))
        {
            options.Enabled = enabled;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismMcpOptions.RefreshInterval)],
                CultureInfo.InvariantCulture,
                out var refreshInterval))
        {
            options.RefreshInterval = refreshInterval;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismMcpOptions.ConnectionTimeout)],
                CultureInfo.InvariantCulture,
                out var connectionTimeout))
        {
            options.ConnectionTimeout = connectionTimeout;
        }

        if (int.TryParse(
                section[nameof(AgentPrismMcpOptions.MaxToolsPerServer)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxToolsPerServer))
        {
            options.MaxToolsPerServer = maxToolsPerServer;
        }

        if (int.TryParse(
                section[nameof(AgentPrismMcpOptions.MaxResourceBytesPerResource)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxResourceBytesPerResource))
        {
            options.MaxResourceBytesPerResource = maxResourceBytesPerResource;
        }

        if (int.TryParse(
                section[nameof(AgentPrismMcpOptions.MaxResourceBytesTotal)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxResourceBytesTotal))
        {
            options.MaxResourceBytesTotal = maxResourceBytesTotal;
        }

        if (section[nameof(AgentPrismMcpOptions.OAuthCallbackBaseUri)] is { Length: > 0 } callbackBaseUri
            && Uri.TryCreate(callbackBaseUri, UriKind.Absolute, out var parsedCallbackBaseUri))
        {
            options.OAuthCallbackBaseUri = parsedCallbackBaseUri;
        }
    }
}
