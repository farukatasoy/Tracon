using System.Text.RegularExpressions;

namespace Tracon;

/// <summary>
/// Conforms MCP tool names to the Tracon registry's naming rules.
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
    /// <returns>
    /// The prefixed name; <see langword="null"/> if the tool name contains an invalid character or the prefixed name is
    /// longer than <see cref="MaxQualifiedNameLength"/>.
    /// </returns>
    /// <remarks>
    /// The registry refuses a name over its limit with an exception, and a list of tools is registered tool by tool: one
    /// name that is too long for it must be skipped here, or its exception takes the whole list of the server with it.
    /// </remarks>
    public static string? TryQualify(string serverName, string toolName)
        => SafeName().IsMatch(toolName) && serverName.Length + 1 + toolName.Length <= MaxQualifiedNameLength
            ? $"{serverName}_{toolName}"
            : null;

    /// <summary>
    /// The longest name the tool registry takes (<c>[A-Za-z0-9_-]{1,64}</c>); a model provider takes no more than that for a
    /// function name either.
    /// </summary>
    public const int MaxQualifiedNameLength = 64;

    [GeneratedRegex("^[a-zA-Z0-9_-]+\\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SafeName();
}
