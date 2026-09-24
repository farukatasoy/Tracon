using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Extensions that register tool discovery from remote MCP servers.
/// </summary>
public static class TraconMcpBuilderExtensions
{
    /// <summary>
    /// Registers the Model Context Protocol client. Tools from every registered
    /// and enabled MCP server are discovered and listed alongside the tools
    /// registered in code.
    /// </summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <strong>Security boundary.</strong> Adding an MCP server means accepting
    /// tool definitions from an external source, and is a deliberate exception
    /// to the code-only tools rule ("tools are defined only in code"). It comes with the
    /// following safeguards:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>Only <em>remote</em> HTTP servers; there is no local process (stdio) transport.</description></item>
    ///   <item><description>MCP tools <strong>require approval</strong> by default.</description></item>
    ///   <item><description>A tool name already registered in code can never be hijacked by MCP.</description></item>
    /// <item>
    /// <description>The server definition carries no secret; the authentication value is resolved from configuration.</description>
    /// </item>
    /// <item>
    /// <description>Every call is written to the <c>tool_invocations</c> table with the source server name.</description>
    /// </item>
    /// </list>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseOpenAI(apiKey)
    ///        .UsePostgreSql(connectionString)
    ///        .UseMcp()
    ///        .UseUI();
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder UseMcp(this ITraconBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return UseMcpCore(builder, configure: null);
    }

    /// <summary>
    /// Registers the Model Context Protocol client with settings given in code.
    /// </summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="configure">The option modifier.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    public static ITraconBuilder UseMcp(
        this ITraconBuilder builder,
        Action<TraconMcpOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        return UseMcpCore(builder, configure);
    }

    /// <summary>
    /// Registers the MCP client, reading options from the <c>Tracon:Mcp</c> section.
    /// </summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="configurationSection">
    /// The section options are read from. Typically
    /// <c>configuration.GetSection(TraconMcpOptions.SectionName)</c>.
    /// </param>
    /// <param name="configure">
    /// The option modifier that runs AFTER the section is bound. A value given
    /// in code overrides the value coming from configuration. Pass
    /// <see langword="null"/> when configuration alone is enough.
    /// </param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> or <paramref name="configurationSection"/>
    /// is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Configuration is provided <strong>explicitly</strong>; Tracon never
    /// reads <c>IConfiguration</c> on its own. This follows the no-surprises rule
    /// and stays consistent with the other <c>Use*</c>
    /// extensions in the repository.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseMcp(builder.Configuration.GetSection(TraconMcpOptions.SectionName), configure: null);
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder UseMcp(
        this ITraconBuilder builder,
        IConfiguration configurationSection,
        Action<TraconMcpOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return UseMcpCore(builder, options =>
        {
            Bind(configurationSection, options);
            configure?.Invoke(options);
        });
    }

    private static ITraconBuilder UseMcpCore(
        ITraconBuilder builder,
        Action<TraconMcpOptions>? configure)
    {
        var services = builder.Services;

        // Validated at startup: RefreshInterval feeds the discovery loop's
        // delay, and a value it refuses stopped the host after startup (F-278).
        services.AddOptions<TraconMcpOptions>().ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<TraconMcpOptions>, TraconMcpOptionsValidator>());

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<McpOAuthTokenCacheRegistry>();
        services.TryAddSingleton<McpToolCatalog>();

        // The registry is REPLACED, not added with TryAdd: AddTracon() runs
        // earlier in the chain and has already registered ToolRegistry. The new
        // registry wraps the old one; tools registered in code keep priority.
        //
        // 🚨 The inner registry is built through ToolRegistry.Create, not a
        // second hand-written construction. Create() also gates the
        // code-defined `generate_image` tool on TraconImageOptions; a
        // duplicated construction here previously skipped that gate, so an
        // enabled image tool silently disappeared whenever MCP was also
        // configured (measured 2026-08-23).
        services.Replace(ServiceDescriptor.Singleton<IToolRegistry>(static provider => new McpToolRegistry(
            ToolRegistry.Create(provider),
            provider.GetRequiredService<McpToolCatalog>(),
            provider.GetRequiredService<ITenantContext>())));

        services.TryAddSingleton<IMcpToolRefresher, McpToolRefresher>();

        // Mode A: AgentDefinitionCompiler (Tracon.Core) resolves this
        // factory as an optional dependency; if it is not registered, a
        // definition using McpResourceUris fails to compile.
        services.TryAddSingleton<IMcpResourceContextProviderFactory, McpResourceContextProviderFactory>();

        // Prompts/Resources (22.1/22.2) and OAuth Mode 1 (22.3): GovernanceEndpoints
        // (Tracon.AspNetCore) resolves these abstractions as optional
        // services; if not registered, the corresponding endpoints return 501.
        services.TryAddSingleton<IMcpPromptClient, McpPromptClient>();
        services.TryAddSingleton<IMcpResourceClient, McpResourceClient>();
        services.TryAddSingleton<IMcpOAuthCoordinator, McpOAuthAuthorizationCoordinator>();

        services.AddHostedService<McpDiscoveryService>();

        return builder;
    }

    /// <summary>
    /// Manually binds the <c>Tracon:Mcp</c> section.
    /// </summary>
    /// <remarks>
    /// Manual binding is an AOT requirement: <c>Bind()</c> relies on
    /// reflection and produces <c>IL2026</c> + <c>IL3050</c>; in trimmed
    /// applications the options would silently stay empty.
    /// When a new option is added to <see cref="TraconMcpOptions"/>, it
    /// must also be added to this method; otherwise the option silently fails
    /// to bind.
    /// </remarks>
    private static void Bind(IConfiguration section, TraconMcpOptions options)
    {
        if (bool.TryParse(section[nameof(TraconMcpOptions.Enabled)], out var enabled))
        {
            options.Enabled = enabled;
        }

        if (TimeSpan.TryParse(
                section[nameof(TraconMcpOptions.RefreshInterval)],
                CultureInfo.InvariantCulture,
                out var refreshInterval))
        {
            options.RefreshInterval = refreshInterval;
        }

        if (TimeSpan.TryParse(
                section[nameof(TraconMcpOptions.ConnectionTimeout)],
                CultureInfo.InvariantCulture,
                out var connectionTimeout))
        {
            options.ConnectionTimeout = connectionTimeout;
        }

        if (int.TryParse(
                section[nameof(TraconMcpOptions.MaxToolsPerServer)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxToolsPerServer))
        {
            options.MaxToolsPerServer = maxToolsPerServer;
        }

        if (int.TryParse(
                section[nameof(TraconMcpOptions.MaxResourceBytesPerResource)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxResourceBytesPerResource))
        {
            options.MaxResourceBytesPerResource = maxResourceBytesPerResource;
        }

        if (int.TryParse(
                section[nameof(TraconMcpOptions.MaxResourceBytesTotal)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxResourceBytesTotal))
        {
            options.MaxResourceBytesTotal = maxResourceBytesTotal;
        }

        if (section[nameof(TraconMcpOptions.OAuthCallbackBaseUri)] is { Length: > 0 } callbackBaseUri
            && Uri.TryCreate(callbackBaseUri, UriKind.Absolute, out var parsedCallbackBaseUri))
        {
            options.OAuthCallbackBaseUri = parsedCallbackBaseUri;
        }
    }
}
