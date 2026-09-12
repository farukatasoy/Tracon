using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Tracon;

/// <summary>
/// Produces the <c>tools/list</c> response from the exposed agents in the catalog.
/// </summary>
/// <remarks>
/// An <see cref="McpRequestHandler{TParams,TResult}"/>; the catalog is read on
/// **every request**, the tool list is not cached. This lets an agent added at
/// runtime appear without setting up a new server.
/// </remarks>
internal static class CatalogToolListHandler
{
    private static readonly JsonElement InputSchema = BuildInputSchema();

    /// <summary>Handles a <c>tools/list</c> request.</summary>
    /// <param name="request">Request context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The tool list produced from the exposed agents.</returns>
    public static async ValueTask<ListToolsResult> HandleAsync(
        RequestContext<ListToolsRequestParams> request,
        CancellationToken cancellationToken)
    {
        var services = RequireServices(request);
        var options = services.GetRequiredService<IOptionsMonitor<TraconMcpServerOptions>>().CurrentValue;
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

    /// <summary>Reads the service provider from the MCP request context.</summary>
    /// <param name="context">Message context.</param>
    /// <returns>The service provider the request is attached to.</returns>
    /// <exception cref="InvalidOperationException">The context carries no service provider.</exception>
    internal static IServiceProvider RequireServices(MessageContext context)
        => context.Services ?? throw new InvalidOperationException(
            "The MCP request context has no service provider. MapTraconMcpServer only works over HTTP transport.");

    private static JsonElement BuildInputSchema()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "type": "object",
              "properties": {
                "message": {
                  "type": "string",
                  "description": "User message to send to the agent."
                }
              },
              "required": ["message"]
            }
            """);

        return document.RootElement.Clone();
    }
}
