using System.Net.Http.Json;
using System.Text.Json;

namespace AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// A small test client that sends a JSON-RPC request over the MCP Streamable
/// HTTP transport.
/// </summary>
/// <remarks>
/// The server may return its response either as a single JSON body or as an
/// SSE frame (per the MCP specification); this helper resolves both.
/// </remarks>
internal static class McpTestClient
{
    /// <summary>Sends a <c>tools/list</c> or <c>tools/call</c> request.</summary>
    /// <param name="client">The HTTP client.</param>
    /// <param name="path">The MCP endpoint.</param>
    /// <param name="method">The JSON-RPC method.</param>
    /// <param name="params">The request parameters; <see langword="null"/> if none.</param>
    /// <param name="token">The bearer token; <see langword="null"/> if none.</param>
    /// <param name="tenantHeader">
    /// The value to write into the <c>X-AgentPrism-Tenant</c> header; <see langword="null"/> if none.
    /// </param>
    /// <returns>The raw HTTP response and the resolved JSON-RPC body.</returns>
    public static async Task<(HttpResponseMessage Response, JsonElement? Body)> SendAsync(
        HttpClient client,
        string path,
        string method,
        object? @params = null,
        string? token = null,
        string? tenantHeader = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method,
                @params,
            }),
        };

        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");

        if (token is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        if (tenantHeader is not null)
        {
            request.Headers.Add("X-AgentPrism-Tenant", tenantHeader);
        }

        var response = await client.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            return (response, null);
        }

        if (string.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.Ordinal))
        {
            await using var stream = await response.Content.ReadAsStreamAsync();
            var frames = await SseReader.ReadAllAsync(stream);
            var last = frames.LastOrDefault(f => f.Data.Length > 0);

            return (response, last is null ? null : JsonDocument.Parse(last.Data).RootElement);
        }

        var text = await response.Content.ReadAsStringAsync();

        return (response, text.Length == 0 ? null : JsonDocument.Parse(text).RootElement);
    }
}
