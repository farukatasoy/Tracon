using System.Globalization;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Mod A: <see cref="AgentDefinition.McpResourceUris"/>'i her cagrida
/// baglama ekleyen <see cref="AIContextProvider"/>.
/// </summary>
/// <remarks>
/// <para>
/// Icerik <see cref="McpConnection"/>'in kendi onbellegi uzerinden okunur;
/// bu saglayici durumsuzdur ve derlenmis agent onbellegiyle (<c>CompiledAgentCache</c>)
/// birlikte uzun sure yasar. Ongorulebilirlik boylece korunur: sunucu
/// degismedigi surece her cagrida ayni icerik girer; degisince abonelik
/// onbellegi gecersiz kilar ve bir sonraki okuma taze ceker (bolum 22.2).
/// </para>
/// <para>
/// Yalnizca sunucunun <c>ListResourcesAsync</c> ile bildirdigi URI'ler
/// okunur; kayitli olmayan bir referans sessizce atlanir ve loglanir.
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
                    "[MCP kaynak toplam boyut siniri asildi; kalan kaynaklar atlandi]");

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
            _logger.LogWarning("MCP kaynak referansi '{Reference}' gecersiz; '{{sunucu}}:{{uri}}' bicimi bekleniyor.", reference);

            return;
        }

        if (!_catalog.TryGetConnection(_tenantId, serverName, out var connection))
        {
            _logger.LogWarning(
                "MCP sunucusu '{ServerName}' su an erisilemiyor; '{Uri}' kaynagi bu calistirmada baglama eklenmeyecek.",
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
                "MCP kaynagi '{ServerName}:{Uri}' okunamadi ({Status}); bu calistirmada baglama eklenmeyecek.",
                serverName,
                uri,
                status);

            return;
        }

        builder.AppendLine(CultureInfo.InvariantCulture, $"### MCP kaynagi: {serverName}:{uri}");

        if (content.IsBinary)
        {
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"[ikili icerik, {content.ByteSize} bayt, {content.MimeType ?? "bilinmeyen tur"}]");
        }
        else
        {
            builder.AppendLine(content.Text);

            if (content.Truncated)
            {
                builder.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"[kirpildi: sinir {perResourceLimit} bayt, ham boyut {content.ByteSize} bayt]");
            }
        }

        builder.AppendLine();
    }
}

/// <summary><see cref="IMcpResourceContextProviderFactory"/> uygulamasi.</summary>
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
