using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism;

/// <summary>
/// Extensions that register the services needed to publish AgentPrism agents
/// as MCP tools.
/// </summary>
public static class AgentPrismMcpServerBuilderExtensions
{
    /// <summary>
    /// Registers the MCP server services. The HTTP endpoint must still be
    /// connected separately with <c>app.MapAgentPrismMcpServer(...)</c>.
    /// </summary>
    /// <param name="builder">The AgentPrism configuration chain.</param>
    /// <param name="configure">Options mutator.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Registration happens HERE, not in <c>MapAgentPrismMcpServer</c>: the MCP
    /// SDK's <c>AddMcpServer()</c> call operates on <see cref="IServiceCollection"/>
    /// and must happen BEFORE the application is <c>Build()</c>-ed. The
    /// `Map...` extensions only connect already-built services to HTTP
    /// (K-251 pattern).
    /// </para>
    /// <para>
    /// By default <strong>no agent is exposed</strong> (K1). Exposing one
    /// requires an explicit choice via
    /// <see cref="AgentPrismMcpServerOptions.ExposedAgents"/> or
    /// <see cref="AgentPrismMcpServerOptions.ExposeAllAgents"/>.
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
