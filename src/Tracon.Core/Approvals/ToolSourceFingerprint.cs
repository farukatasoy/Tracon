using System.Security.Cryptography;
using System.Text;

namespace Tracon;

/// <summary>
/// Names the remote source a tool comes from, so that a standing approval stays
/// with the source it was given for.
/// </summary>
/// <remarks>
/// <para>
/// An MCP tool is named <c>{server}_{tool}</c>. The server record, its endpoint
/// included, is written by one role (<c>AgentsAdmin</c>); a standing approval rule
/// is written by another (<c>SecurityAdmin</c>). Pointing the same server name at
/// another endpoint keeps every tool name, so a rule that named only the tool
/// approved a tool of an endpoint it was never given for. A rule now carries this
/// fingerprint (<see cref="ToolApprovalRule.SourceFingerprint"/>) and matches only
/// while the tool's current source has the same one.
/// </para>
/// <para>
/// The value is a hash, not the address: an endpoint can carry a credential in its
/// query string, and the rule table must not become a second copy of it.
/// </para>
/// </remarks>
internal static class ToolSourceFingerprint
{
    /// <summary>
    /// The value a registry reports for a tool that belongs to a remote source whose
    /// tools it cannot see right now (the server's last refresh failed).
    /// </summary>
    /// <remarks>
    /// It is not a fingerprint: it matches no stored rule, so such a call asks for
    /// approval, and it is never written to a rule. Reporting <see langword="null"/>
    /// instead would read as "a tool defined in code" and let every rule without a
    /// source approve a remote call that a compiled agent can still make.
    /// </remarks>
    public const string Unresolved = "unresolved-remote-source";

    /// <summary>Says whether a value is a fingerprint a rule may record.</summary>
    /// <param name="fingerprint">The value a registry reported.</param>
    /// <returns><see langword="false"/> for <see cref="Unresolved"/>; otherwise <see langword="true"/>.</returns>
    public static bool IsRecordable(string? fingerprint)
        => !string.Equals(fingerprint, Unresolved, StringComparison.Ordinal);

    /// <summary>Computes the fingerprint of a remote endpoint.</summary>
    /// <param name="endpoint">The absolute endpoint address.</param>
    /// <returns>The SHA-256 of the address, as 64 upper-case hexadecimal characters.</returns>
    public static string ForEndpoint(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint.AbsoluteUri)));
    }

    /// <summary>Reads the current source fingerprint of a tool.</summary>
    /// <param name="registry">The tool registry that serves the tool.</param>
    /// <param name="tenantId">The tenant the tool is resolved for.</param>
    /// <param name="toolName">The tool name.</param>
    /// <returns>
    /// The fingerprint, or <see langword="null"/> when the tool does not come from a
    /// remote source (a tool defined in code), when it is not known, or when the
    /// registry cannot tell.
    /// </returns>
    public static string? Of(IToolRegistry registry, string tenantId, string toolName)
    {
        ArgumentNullException.ThrowIfNull(registry);

        return registry is IToolSourceResolver sources
            ? sources.GetSourceFingerprint(tenantId, toolName)
            : null;
    }
}

/// <summary>
/// Implemented by a tool registry that serves tools from a remote source.
/// </summary>
/// <remarks>
/// The registry that resolves a tool is the only party that knows where it came
/// from, so the approval rule evaluator and the paths that write a rule ask it
/// through <see cref="ToolSourceFingerprint.Of"/>. A registry that does not
/// implement this interface serves code tools only.
/// </remarks>
internal interface IToolSourceResolver
{
    /// <summary>Reads the current source fingerprint of a tool.</summary>
    /// <param name="tenantId">The tenant the tool is resolved for.</param>
    /// <param name="toolName">The tool name.</param>
    /// <returns>
    /// The fingerprint (<see cref="ToolSourceFingerprint.ForEndpoint"/>);
    /// <see cref="ToolSourceFingerprint.Unresolved"/> when the tool belongs to a remote
    /// source whose tools are not visible right now; <see langword="null"/> only when
    /// the tool does not come from a remote source.
    /// </returns>
    string? GetSourceFingerprint(string tenantId, string toolName);
}
