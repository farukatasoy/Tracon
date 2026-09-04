using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Connects a real <see cref="McpClient"/> — the same SDK type a real MCP host
/// uses — to the in-process test server, with the Tasks extension capability
/// declared. Phase 117's task-mode behavior depends on protocol version
/// negotiation and per-request capability metadata that only the SDK's own
/// client gets right; a hand-rolled JSON-RPC POST (<see cref="McpTestClient"/>)
/// is not enough for these tests.
/// </summary>
internal static class McpTaskTestClient
{
    /// <summary>Connects to the given MCP endpoint, with the Tasks extension declared.</summary>
    /// <param name="client">The in-process test host's client.</param>
    /// <param name="path">The MCP endpoint path.</param>
    /// <param name="token">The bearer token; <see langword="null"/> if none.</param>
    /// <param name="tenantHeader">The tenant header value; <see langword="null"/> if none.</param>
    /// <returns>The connected client. The caller disposes it.</returns>
    public static async Task<McpClient> ConnectAsync(
        HttpClient client, string path, string? token = null, string? tenantHeader = null)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);

        if (token is not null)
        {
            headers["Authorization"] = $"Bearer {token}";
        }

        if (tenantHeader is not null)
        {
            headers["X-AgentPrism-Tenant"] = tenantHeader;
        }

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(client.BaseAddress!, path),
                AdditionalHeaders = headers,
            },
            client,
            ownsHttpClient: false);

        return await McpClient.CreateAsync(
            transport,
            new McpClientOptions
            {
                Capabilities = new ClientCapabilities
                {
                    Extensions = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["io.modelcontextprotocol/tasks"] = new object(),
                    },
                },
                // F-190: the SDK's production default (5s) is tuned for real
                // network peers. Under the full-package CI run's CPU
                // contention, this in-memory TestServer can occasionally miss
                // that window; the SDK then silently falls back to the
                // legacy `initialize` handshake and negotiates 2025-11-25,
                // which the Tasks extension rejects. 30s stays safely under
                // McpClientOptions.InitializationTimeout's 60s default.
                DiscoverProbeTimeout = TimeSpan.FromSeconds(30),
            });
    }
}
