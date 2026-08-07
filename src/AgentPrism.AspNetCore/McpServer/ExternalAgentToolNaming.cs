namespace AgentPrism;

/// <summary>
/// Katalogdaki bir agent adi ile MCP tool adi arasinda donusum yapar.
/// </summary>
/// <remarks>
/// Yon, Faz 22'nin <c>McpToolNaming</c>'inin (uzak sunucu tool'lari icin
/// <c>{sunucu}_{tool}</c> uretir) aynasidir: burada <c>{onek}_{agent}</c> uretilir.
/// Mantik kasitli olarak yeniden yazilir; <c>AgentPrism.Mcp</c> istemci paketine
/// sunucu tarafindan bagimlilik eklenmez (K-057'nin bagimlilik yonu).
/// </remarks>
internal static class ExternalAgentToolNaming
{
    /// <summary>Bir agent adindan MCP tool adi uretir.</summary>
    /// <param name="toolNamePrefix">Yapilandirilan onek.</param>
    /// <param name="agentName">Agent adi.</param>
    /// <returns>Onekli tool adi.</returns>
    public static string ToToolName(string toolNamePrefix, string agentName)
        => $"{toolNamePrefix}_{agentName}";

    /// <summary>Bir MCP tool adindan agent adini cozer.</summary>
    /// <param name="toolNamePrefix">Yapilandirilan onek.</param>
    /// <param name="toolName">Gelen tool adi.</param>
    /// <param name="agentName">Cozulen agent adi.</param>
    /// <returns>Ad onekle basliyorsa ve bos degilse <see langword="true"/>.</returns>
    public static bool TryParseAgentName(string toolNamePrefix, string toolName, out string agentName)
    {
        var expectedPrefix = toolNamePrefix + "_";

        if (toolName.StartsWith(expectedPrefix, StringComparison.Ordinal) && toolName.Length > expectedPrefix.Length)
        {
            agentName = toolName[expectedPrefix.Length..];
            return true;
        }

        agentName = "";
        return false;
    }
}
