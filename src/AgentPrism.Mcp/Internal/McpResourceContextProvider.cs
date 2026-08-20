using System.Globalization;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Mode A: an <see cref="AIContextProvider"/> that adds
/// <see cref="AgentDefinition.McpResourceUris"/> to the context on every call.
/// </summary>
/// <remarks>
/// <para>
/// Content is read through <see cref="McpConnection"/>'s own cache; this
/// provider is stateless and lives long alongside the compiled agent cache
/// (<c>CompiledAgentCache</c>). Predictability is preserved this way: as
/// long as the server does not change, the same content is added on every
/// call; when it changes, the subscription invalidates the cache and the
/// next read fetches it fresh.
/// </para>
/// <para>
/// Only URIs the server declares via <c>ListResourcesAsync</c> are read; an
/// undeclared reference is silently skipped and logged.
/// </para>
/// </remarks>
internal sealed class McpResourceContextProvider : AIContextProvider
{
    private readonly IReadOnlyList<string> _resourceReferences;
    private readonly string _tenantId;
    private readonly McpToolCatalog _catalog;
    private readonly IOptions<AgentPrismMcpOptions> _options;
    private readonly ILogger _logger;

    public McpResourceContextProvider(
        IReadOnlyList<string> resourceReferences,
        string tenantId,
        McpToolCatalog catalog,
        IOptions<AgentPrismMcpOptions> options,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(resourceReferences);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _resourceReferences = resourceReferences;
        _tenantId = tenantId;
        _catalog = catalog;
        _options = options;
        _logger = loggerFactory.CreateLogger<McpResourceContextProvider>();
    }

    /// <inheritdoc />
    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var options = _options.Value;
        var builder = new StringBuilder();
        var remainingBudget = options.MaxResourceBytesTotal;

        foreach (var reference in _resourceReferences)
        {
            if (remainingBudget <= 0)
            {
                builder.AppendLine(
                    "[MCP resource total size limit exceeded; remaining resources skipped]");

                break;
            }

            await AppendResourceAsync(reference, builder, options, remainingBudget, cancellationToken)
                .ConfigureAwait(false);

            remainingBudget = options.MaxResourceBytesTotal - Encoding.UTF8.GetByteCount(builder.ToString());
        }

        return new AIContext { Instructions = builder.Length > 0 ? builder.ToString() : null };
    }

    private async ValueTask AppendResourceAsync(
        string reference,
        StringBuilder builder,
        AgentPrismMcpOptions options,
        int remainingBudget,
        CancellationToken cancellationToken)
    {
        if (!McpResourceReference.TryParse(reference, out var serverName, out var uri))
        {
            _logger.LogWarning("MCP resource reference '{Reference}' is invalid; expected format is '{{server}}:{{uri}}'.", reference);

            return;
        }

        if (!_catalog.TryGetConnection(_tenantId, serverName, out var connection))
        {
            _logger.LogWarning(
                "MCP server '{ServerName}' is currently unreachable; resource '{Uri}' will not be added to context for this run.",
                serverName,
                uri);

            return;
        }

        var perResourceLimit = Math.Min(options.MaxResourceBytesPerResource, remainingBudget);

        var (status, content) = await connection
            .ReadDeclaredResourceAsync(uri, perResourceLimit, options, _logger, cancellationToken)
            .ConfigureAwait(false);

        if (status != McpOperationStatus.Ok || content is null)
        {
            _logger.LogWarning(
                "Could not read MCP resource '{ServerName}:{Uri}' ({Status}); it will not be added to context for this run.",
                serverName,
                uri,
                status);

            return;
        }

        builder.AppendLine(CultureInfo.InvariantCulture, $"### MCP resource: {serverName}:{uri}");

        if (content.IsBinary)
        {
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"[binary content, {content.ByteSize} bytes, {content.MimeType ?? "unknown type"}]");
        }
        else
        {
            builder.AppendLine(content.Text);

            if (content.Truncated)
            {
                builder.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"[truncated: limit {perResourceLimit} bytes, raw size {content.ByteSize} bytes]");
            }
        }

        builder.AppendLine();
    }
}

/// <summary>The <see cref="IMcpResourceContextProviderFactory"/> implementation.</summary>
internal sealed class McpResourceContextProviderFactory : IMcpResourceContextProviderFactory
{
    private readonly McpToolCatalog _catalog;
    private readonly IOptions<AgentPrismMcpOptions> _options;
    private readonly ILoggerFactory _loggerFactory;

    public McpResourceContextProviderFactory(
        McpToolCatalog catalog,
        IOptions<AgentPrismMcpOptions> options,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _catalog = catalog;
        _options = options;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public AIContextProvider Create(IReadOnlyList<string> resourceReferences, string tenantId)
        => new McpResourceContextProvider(resourceReferences, tenantId, _catalog, _options, _loggerFactory);
}
