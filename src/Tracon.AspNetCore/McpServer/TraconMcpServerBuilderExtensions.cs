using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Server;

namespace Tracon;

/// <summary>
/// Extensions that register the services needed to publish Tracon agents
/// as MCP tools.
/// </summary>
public static class TraconMcpServerBuilderExtensions
{
    /// <summary>
    /// Registers the MCP server services. The HTTP endpoint must still be
    /// connected separately with <c>app.MapTraconMcpServer(...)</c>.
    /// </summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="configure">Options mutator.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Registration happens HERE, not in <c>MapTraconMcpServer</c>: the MCP
    /// SDK's <c>AddMcpServer()</c> call operates on <see cref="IServiceCollection"/>
    /// and must happen BEFORE the application is <c>Build()</c>-ed. The
    /// `Map...` extensions only connect already-built services to HTTP.
    /// </para>
    /// <para>
    /// By default <strong>no agent is exposed</strong>. Exposing one
    /// requires an explicit choice via
    /// <see cref="TraconMcpServerOptions.ExposedAgents"/> or
    /// <see cref="TraconMcpServerOptions.ExposeAllAgents"/>.
    /// </para>
    /// <para>
    /// Also registers the MCP Tasks extension, gated OFF by default
    /// through <see cref="TraconMcpServerOptions.EnableTasks"/>: the
    /// execution-mode selector returns <see cref="McpTaskExecutionMode.Synchronous"/>
    /// when it is off, which is the SDK's own fast path back to today's
    /// behavior — <c>CreateTaskAsync</c> is never called. The task-provisioning
    /// filter (<see cref="McpTaskRunProvisioningFilter"/>) is still registered
    /// unconditionally; it is value-transparent when tasks are off, since it
    /// only replaces <see cref="CatalogToolCallHandler"/>'s own id-minting with
    /// an equivalent one handed down ambiently.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseOpenAI(apiKey)
    ///        .UsePostgreSql(connectionString)
    ///        .UseMcpServer(o => o.ExposedAgents.Add("asistan"));
    /// // ...
    /// app.MapTracon("/tracon");
    /// app.MapTraconMcpServer();
    /// </code>
    /// </example>
    /// </remarks>
#pragma warning disable MCPEXP002
    public static ITraconBuilder UseMcpServer(
        this ITraconBuilder builder,
        Action<TraconMcpServerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        services.AddOptions<TraconMcpServerOptions>();

        if (configure is not null)
        {
            services.Configure(configure);
        }

        // Registered BEFORE .WithTasks(...) below: McpRequestFilters.CallToolWithAlternateFilters
        // runs in registration order, so this filter (which provisions the run
        // id every task-mode call will use) runs ahead of the Tasks
        // extension's own filter, which is exactly what CreateTaskAsync needs.
        services.Configure<McpServerOptions>(McpTaskRunProvisioningFilter.Register);

        var taskStore = new RunBackedMcpTaskStore();
        services.AddSingleton(taskStore);

        services.AddMcpServer()
            .WithHttpTransport()
            .WithListToolsHandler(CatalogToolListHandler.HandleAsync)
            .WithCallToolHandler(CatalogToolCallHandler.HandleAsync)
            .WithTasks(taskStore, taskOptions => taskOptions.ExecutionModeSelector = SelectExecutionMode);

        return builder;
    }

    private static McpTaskExecutionMode SelectExecutionMode(RequestContext<ModelContextProtocol.Protocol.CallToolRequestParams> request)
    {
        var services = CatalogToolListHandler.RequireServices(request);
        var enabled = services.GetRequiredService<IOptionsMonitor<TraconMcpServerOptions>>().CurrentValue.EnableTasks;

        // K1: a new expansion point defaults off, zero surprises.
        // Optional, not Required, once enabled: Required would break a client
        // that never opted into the tasks capability; Optional keeps today's
        // synchronous response for such a client while still letting a
        // task-aware client opt into polling (Open Question 3, option A).
        return enabled ? McpTaskExecutionMode.Optional : McpTaskExecutionMode.Synchronous;
    }
}
#pragma warning restore MCPEXP002
