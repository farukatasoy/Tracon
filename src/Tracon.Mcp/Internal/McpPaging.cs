using System.Runtime.CompilerServices;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Tracon;

/// <summary>
/// Reads an MCP list one page at a time, so a reader can stop before the server's list ends.
/// </summary>
/// <remarks>
/// The SDK's collecting overloads (<c>ListToolsAsync(RequestOptions)</c> and its resource and prompt siblings) follow the
/// cursor until the server stops handing one out and return the whole list. A server that never stops filled memory
/// (3 GB in ten seconds) before any limit was looked at. These readers fetch a page only when the caller asks for an item
/// of it, so a caller that stops at a limit also stops the cursor. A single page is still read whole.
/// </remarks>
internal static class McpPaging
{
    /// <summary>Reads the tools of a server.</summary>
    /// <param name="client">The connected client.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tools, page by page.</returns>
    public static IAsyncEnumerable<Tool> ToolsAsync(McpClient client, CancellationToken cancellationToken)
        => ItemsAsync(
            async (cursor, token) =>
            {
                var page = await client.ListToolsAsync(new ListToolsRequestParams { Cursor = cursor }, token).ConfigureAwait(false);

                return (page.Tools, page.NextCursor);
            },
            cancellationToken);

    /// <summary>Reads the resources of a server.</summary>
    /// <param name="client">The connected client.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resources, page by page.</returns>
    public static IAsyncEnumerable<Resource> ResourcesAsync(McpClient client, CancellationToken cancellationToken)
        => ItemsAsync(
            async (cursor, token) =>
            {
                var page = await client.ListResourcesAsync(new ListResourcesRequestParams { Cursor = cursor }, token).ConfigureAwait(false);

                return (page.Resources, page.NextCursor);
            },
            cancellationToken);

    /// <summary>Reads the prompts of a server.</summary>
    /// <param name="client">The connected client.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The prompts, page by page.</returns>
    public static IAsyncEnumerable<Prompt> PromptsAsync(McpClient client, CancellationToken cancellationToken)
        => ItemsAsync(
            async (cursor, token) =>
            {
                var page = await client.ListPromptsAsync(new ListPromptsRequestParams { Cursor = cursor }, token).ConfigureAwait(false);

                return (page.Prompts, page.NextCursor);
            },
            cancellationToken);

    /// <summary>
    /// Reads at most <paramref name="limit"/> items.
    /// </summary>
    /// <typeparam name="TItem">The item type.</typeparam>
    /// <param name="items">The items, page by page.</param>
    /// <param name="limit">The most items to keep.</param>
    /// <returns>
    /// The first <paramref name="limit"/> items, and whether the server had more: the read ends with the first item past the
    /// limit, so the cursor is not followed any further.
    /// </returns>
    public static async ValueTask<(List<TItem> Kept, bool Exceeded)> TakeAsync<TItem>(IAsyncEnumerable<TItem> items, int limit)
    {
        var kept = new List<TItem>();

        await foreach (var item in items.ConfigureAwait(false))
        {
            if (kept.Count >= limit)
            {
                return (kept, true);
            }

            kept.Add(item);
        }

        return (kept, false);
    }

    private static async IAsyncEnumerable<TItem> ItemsAsync<TItem>(
        Func<string?, CancellationToken, ValueTask<(IList<TItem> Items, string? NextCursor)>> readPage,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? cursor = null;

        do
        {
            var (items, nextCursor) = await readPage(cursor, cancellationToken).ConfigureAwait(false);

            foreach (var item in items)
            {
                yield return item;
            }

            cursor = nextCursor;
        }
        while (cursor is not null);
    }
}
