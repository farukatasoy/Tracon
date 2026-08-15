using System.Text.RegularExpressions;

namespace AgentPrism;

/// <summary>
/// Conforms MCP tool names to the AgentPrism registry's naming rules.
/// </summary>
/// <remarks>
/// <para>
/// The name is put in the form <c>{server}_{tool}</c>. The prefix is
/// mandatory: it is common for two different servers to have a tool with
/// the same name, and without the prefix one would hide the other.
/// </para>
/// <para>
/// <strong>No dot is used.</strong> OpenAI and compatible providers accept
/// only <c>[a-zA-Z0-9_-]</c> in function names; a <c>server.tool</c> form
/// would be rejected by the provider at call time.
/// </para>
/// </remarks>
internal static partial class McpToolNaming
{
    /// <summary>Reports whether a server name can be used in a tool name.</summary>
    /// <param name="serverName">The server name.</param>
    /// <returns><see langword="true"/> if the name is valid.</returns>
    public static bool IsValidServerName(string? serverName)
        => !string.IsNullOrWhiteSpace(serverName) && SafeName().IsMatch(serverName);

    /// <summary>Produces the name to use in the registry from the server and tool names.</summary>
    /// <param name="serverName">The server name.</param>
    /// <param name="toolName">The tool name on the remote server.</param>
    /// <returns>The prefixed name; <see langword="null"/> if the tool name contains an invalid character.</returns>
    public static string? TryQualify(string serverName, string toolName)
        => SafeName().IsMatch(toolName) ? $"{serverName}_{toolName}" : null;

    [GeneratedRegex("^[a-zA-Z0-9_-]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SafeName();
}
