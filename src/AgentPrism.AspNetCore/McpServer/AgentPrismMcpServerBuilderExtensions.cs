using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism;

/// <summary>
/// AgentPrism agent'larini MCP tool'u olarak yayimlamak icin servis kaydini yapan
/// uzantilar.
/// </summary>
public static class AgentPrismMcpServerBuilderExtensions
{
    /// <summary>
    /// MCP sunucu servislerini kaydeder. HTTP ucu ayrica
    /// <c>app.MapAgentPrismMcpServer(...)</c> ile baglanmalidir.
    /// </summary>
    /// <param name="builder">AgentPrism yapilandirma zinciri.</param>
    /// <param name="configure">Ayar degistirici.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> <see langword="null"/> ise.</exception>
    /// <remarks>
    /// <para>
    /// Kayit BURADA yapilir, <c>MapAgentPrismMcpServer</c>'da degil: MCP SDK'sinin
    /// <c>AddMcpServer()</c> cagrisi <see cref="IServiceCollection"/> uzerinde
    /// calisir ve uygulama <c>Build()</c> olmadan ONCE yapilmalidir. `Map...`
    /// uzantilari yalniz zaten kurulmus servisleri HTTP'ye baglar (K-251 deseni).
    /// </para>
    /// <para>
    /// Varsayilan olarak <strong>hicbir agent disa acik degildir</strong> (K1).
    /// Disa acmak <see cref="AgentPrismMcpServerOptions.ExposedAgents"/> veya
    /// <see cref="AgentPrismMcpServerOptions.ExposeAllAgents"/> ile acik bir
    /// tercih gerektirir.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
    ///        .UseOpenAI(apiKey)
    ///        .UsePostgreSql(connectionString)
    ///        .UseMcpServer(o => o.ExposedAgents.Add("asistan"));
    /// // ...
    /// app.MapAgentPrism("/agentprism");
    /// app.MapAgentPrismMcpServer();
    /// </code>
    /// </example>
    /// </remarks>
    public static IAgentPrismBuilder UseMcpServer(
        this IAgentPrismBuilder builder,
        Action<AgentPrismMcpServerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        services.AddOptions<AgentPrismMcpServerOptions>();

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddMcpServer()
            .WithHttpTransport()
            .WithListToolsHandler(CatalogToolListHandler.HandleAsync)
            .WithCallToolHandler(CatalogToolCallHandler.HandleAsync);

        return builder;
    }
}
