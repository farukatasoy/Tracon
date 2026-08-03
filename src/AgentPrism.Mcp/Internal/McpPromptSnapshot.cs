using System.Security.Cryptography;
using System.Text;
using ModelContextProtocol.Protocol;

namespace AgentPrism;

/// <summary>
/// Bir <see cref="GetPromptResult"/>'tan anlik goruntu (metin + ozet) kurar.
/// </summary>
/// <remarks>
/// Saf ve durumsuzdur; agi gerektirmez, dogrudan birim testiyle dogrulanir.
/// Hash, arayuzun <c>mcp.prompt.hash</c> metadata'siyla karsilastirip sunucudaki
/// degisikligi rozet olarak gostermesi icindir (bolum 22.1).
/// </remarks>
internal static class McpPromptSnapshot
{
    /// <summary>Prompt mesajlarini birlestirir ve SHA-256 ozetini hesaplar.</summary>
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
