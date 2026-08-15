namespace AgentPrism;

/// <summary>
/// Converts between an agent name in the catalog and an MCP tool name.
/// </summary>
/// <remarks>
/// The direction mirrors Phase 22's <c>McpToolNaming</c> (which produces
/// <c>{server}_{tool}</c> for remote server tools): here it produces
/// <c>{prefix}_{agent}</c>. The logic is deliberately re-implemented; no
/// dependency on the <c>AgentPrism.Mcp</c> client package is added from the
/// server side (K-057's dependency direction).
/// </remarks>
internal static class ExternalAgentToolNaming
{
    /// <summary>Produces an MCP tool name from an agent name.</summary>
    /// <param name="toolNamePrefix">The configured prefix.</param>
    /// <param name="agentName">Agent name.</param>
    /// <returns>The prefixed tool name.</returns>
    public static string ToToolName(string toolNamePrefix, string agentName)
        => $"{toolNamePrefix}_{agentName}";

    /// <summary>Resolves an agent name from an MCP tool name.</summary>
    /// <param name="toolNamePrefix">The configured prefix.</param>
    /// <param name="toolName">Incoming tool name.</param>
    /// <param name="agentName">The resolved agent name.</param>
    /// <returns><see langword="true"/> if the name starts with the prefix and is not empty.</returns>
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
