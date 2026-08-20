using System.Security.Cryptography;
using System.Text;
using ModelContextProtocol.Protocol;

namespace AgentPrism;

/// <summary>
/// Builds a snapshot, text and digest, from <see cref="GetPromptResult"/>.
/// </summary>
/// <remarks>
/// It is pure and stateless, needs no network, and is verified by direct unit tests.
/// The UI compares the hash with <c>mcp.prompt.hash</c> metadata and shows a badge
/// for a server-side change.
/// </remarks>
internal static class McpPromptSnapshot
{
    /// <summary>Combines prompt messages and calculates their SHA-256 digest.</summary>
    public static McpPromptContent Build(GetPromptResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var text = string.Join(
            "\n\n",
            result.Messages
                .Select(static message => message.Content is TextContentBlock textBlock ? textBlock.Text : null)
                .Where(static text => !string.IsNullOrEmpty(text)));

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

        return new McpPromptContent { Text = text, Hash = hash };
    }
}
