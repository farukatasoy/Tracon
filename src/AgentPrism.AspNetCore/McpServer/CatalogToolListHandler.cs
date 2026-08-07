using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AgentPrism;

/// <summary>
/// <c>tools/list</c> istegini katalogdaki disa acik agent'lardan uretir.
/// </summary>
/// <remarks>
/// Bir <see cref="McpRequestHandler{TParams,TResult}"/>'tir; katalog **her istekte**
/// okunur, tool listesi yayimlanmaz. Bu, calisma aninda eklenen bir agent'in yeni bir
/// sunucu kurulmadan gorunmesini saglar (bolum 50.3).
/// </remarks>
internal static class CatalogToolListHandler
{
    private static readonly JsonElement InputSchema = BuildInputSchema();

    /// <summary><c>tools/list</c> isteğini isler.</summary>
    /// <param name="request">Istek baglami.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Disa acik agent'lardan uretilen tool listesi.</returns>
    public static async ValueTask<ListToolsResult> HandleAsync(
        RequestContext<ListToolsRequestParams> request,
        CancellationToken cancellationToken)
    {
        var services = RequireServices(request);
        var options = services.GetRequiredService<IOptionsMonitor<AgentPrismMcpServerOptions>>().CurrentValue;
        var catalog = services.GetRequiredService<IAgentCatalog>();

        var descriptors = await catalog.ListAsync(cancellationToken).ConfigureAwait(false);
        var tools = new List<Tool>();

        foreach (var descriptor in descriptors)
        {
            if (!ExternalSurfaceGuard.IsExposed(descriptor.Name, options.ExposedAgents, options.ExposeAllAgents))
            {
                continue;
            }

            tools.Add(new Tool
            {
                Name = ExternalAgentToolNaming.ToToolName(options.ToolNamePrefix, descriptor.Name),
                Description = descriptor.Description ?? descriptor.DisplayName ?? descriptor.Name,
                InputSchema = InputSchema,
            });
        }

        return new ListToolsResult { Tools = tools };
    }

    /// <summary>MCP istek baglamindan servis saglayiciyi okur.</summary>
    /// <param name="context">Mesaj baglami.</param>
    /// <returns>Istegin bagli oldugu servis saglayici.</returns>
    /// <exception cref="InvalidOperationException">Baglam servis saglayici tasimiyorsa.</exception>
    internal static IServiceProvider RequireServices(MessageContext context)
        => context.Services ?? throw new InvalidOperationException(
            "MCP istek baglaminda servis saglayici yok. MapAgentPrismMcpServer yalniz HTTP tasimasi uzerinden calisir.");

    private static JsonElement BuildInputSchema()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "type": "object",
              "properties": {
                "message": {
                  "type": "string",
                  "description": "Agent'a gonderilecek kullanici mesaji."
                }
              },
              "required": ["message"]
            }
            """);

        return document.RootElement.Clone();
    }
}
