using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// <see cref="AgentDefinition.McpResourceUris"/> (Mod A) icin bir
/// <see cref="AIContextProvider"/> kuran fabrika.
/// </summary>
/// <remarks>
/// <para>
/// Soyutlama <c>AgentPrism.Abstractions</c> icindedir cunku <c>AgentDefinitionCompiler</c>
/// (<c>AgentPrism.Core</c>) MCP kaynaklarini baglama eklemek ister ancak
/// <c>AgentPrism.Mcp</c> paketine bagli degildir: MCP istege bagli bir paket
/// olarak kalir. Gerekce <see cref="IMcpToolRefresher"/> ile aynidir.
/// </para>
/// </remarks>
public interface IMcpResourceContextProviderFactory
{
    /// <summary>
    /// Verilen kaynak referanslariyla bir baglam saglayicisi kurar.
    /// </summary>
    /// <param name="resourceReferences">
    /// <c>"{sunucu}:{uri}"</c> bicimli referanslar (<see cref="AgentDefinition.McpResourceUris"/>).
    /// </param>
    /// <param name="tenantId">Kiraci kimligi. Kaynaklar yalnizca bu kiracinin sunucularindan okunur.</param>
    /// <returns>Baglam saglayicisi.</returns>
    AIContextProvider Create(IReadOnlyList<string> resourceReferences, string tenantId);
}
