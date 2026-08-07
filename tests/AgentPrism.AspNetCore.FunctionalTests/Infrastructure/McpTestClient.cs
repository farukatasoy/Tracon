using System.Net.Http.Json;
using System.Text.Json;

namespace AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// MCP Streamable HTTP tasimasi uzerinden JSON-RPC istegi gonderen kucuk bir test
/// istemcisi.
/// </summary>
/// <remarks>
/// Sunucu yanitini ya tek bir JSON govdesi ya da bir SSE cercevesi olarak
/// dondurebilir (MCP spesifikasyonu); bu yardimci ikisini de cozer.
/// </remarks>
internal static class McpTestClient
{
    /// <summary>Bir <c>tools/list</c> veya <c>tools/call</c> istegi gonderir.</summary>
    /// <param name="client">HTTP istemcisi.</param>
    /// <param name="path">MCP ucu.</param>
    /// <param name="method">JSON-RPC metodu.</param>
    /// <param name="params">Istek parametreleri; yoksa <see langword="null"/>.</param>
    /// <param name="token">Bearer token; yoksa <see langword="null"/>.</param>
    /// <param name="tenantHeader">
    /// <c>X-AgentPrism-Tenant</c> basligina yazilacak deger; yoksa <see langword="null"/>.
    /// </param>
    /// <returns>Ham HTTP yaniti ve cozumlenmis JSON-RPC govdesi.</returns>
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
