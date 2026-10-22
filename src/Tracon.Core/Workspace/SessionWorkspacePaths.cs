using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Tracon;

/// <summary>
/// The shape of the paths of the session workspace, in the one place that writes them.
/// </summary>
/// <remarks>
/// <para>
/// A file of a workspace lives at <c>{tenant}/~workspace/{key}/{agent}/{path}</c> in the agent file
/// store. The key comes first so that everything one session owns, whichever agent wrote it, is
/// one directory: an erasure of a session names only the tenant and the session id (it does not
/// know the agent) and deletes <c>{tenant}/~workspace/{key}</c>.
/// </para>
/// <para>
/// The shape is permanent. A workspace written under one shape cannot be found under another; a
/// change needs a move of the stored files.
/// </para>
/// </remarks>
internal static class SessionWorkspacePaths
{
    /// <summary>
    /// The directory below the tenant that holds every workspace. An agent cannot have this name,
    /// because the file memory of an agent with this name would be the directory of every workspace.
    /// </summary>
    internal const string RootName = "~workspace";

    /// <summary>
    /// The longest path the store accepts, with its leading <c>/</c>: the narrowest <c>agent_files.path</c>
    /// column (SQL Server, <c>nvarchar(400)</c>).
    /// </summary>
    internal const int MaxPathLength = 400;

    /// <summary>How many hexadecimal characters of the digest the key keeps (128 bits).</summary>
    private const int KeyLength = 32;

    /// <summary>Gets the directory of all the workspaces of a tenant.</summary>
    /// <param name="tenantId">The tenant, in any letter case.</param>
    /// <returns>The directory, with no leading <c>/</c>.</returns>
    internal static string TenantRoot(string tenantId)
        => AmbientTenantScope.Normalize(tenantId) + "/" + RootName;

    /// <summary>Gets the directory of everything one session owns.</summary>
    /// <param name="tenantId">The tenant, in any letter case.</param>
    /// <param name="sessionId">The session.</param>
    /// <returns>The directory, with no leading <c>/</c>.</returns>
    internal static string SessionRoot(string tenantId, string sessionId)
        => TenantRoot(tenantId) + "/" + Key(tenantId, sessionId);

    /// <summary>Gets the directory of the workspace one agent has in one session.</summary>
    /// <param name="tenantId">The tenant, in any letter case.</param>
    /// <param name="sessionId">The session.</param>
    /// <param name="agentName">The agent.</param>
    /// <returns>The directory, with no leading <c>/</c>.</returns>
    internal static string AgentRoot(string tenantId, string sessionId, string agentName)
        => SessionRoot(tenantId, sessionId) + "/" + agentName;

    /// <summary>
    /// Makes the key of a session: the first 32 hexadecimal characters of the SHA-256 digest of the
    /// tenant and the session id.
    /// </summary>
    /// <param name="tenantId">The tenant, in any letter case.</param>
    /// <param name="sessionId">The session.</param>
    /// <returns>The key, in lower case.</returns>
    /// <remarks>
    /// The session id does not appear in the path: it is unique inside one tenant only, it can be long, and
    /// a path is what the model reads in a search result. Each part carries its length, so no two pairs
    /// make the same input.
    /// </remarks>
    internal static string Key(string tenantId, string sessionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        var tenant = Encoding.UTF8.GetBytes(AmbientTenantScope.Normalize(tenantId));
        var session = Encoding.UTF8.GetBytes(sessionId);
        var input = new byte[8 + tenant.Length + session.Length];

        // Little-endian on every machine: the key is permanent, and a workspace written on one host must be found on another.
        BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(0, 4), tenant.Length);
        tenant.CopyTo(input, 4);
        BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(4 + tenant.Length, 4), session.Length);
        session.CopyTo(input, 8 + tenant.Length);

        return Convert.ToHexString(SHA256.HashData(input))[..KeyLength].ToLowerInvariant();
    }

    /// <summary>
    /// Cleans a path the agent wrote: backslashes become slashes, a leading or trailing slash goes, and a
    /// path that leaves the workspace or holds a control character is refused.
    /// </summary>
    /// <param name="path">The path as the agent wrote it. Empty means the root of the workspace.</param>
    /// <returns>The path below the root of the workspace, with no leading <c>/</c>; empty for the root.</returns>
    /// <exception cref="TraconException">The path holds a <c>.</c> or <c>..</c> segment or a control character.</exception>
    internal static string Relative(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var segments = new List<string>();

        foreach (var segment in path.Replace('\\', '/').Split('/'))
        {
            if (segment.Length == 0)
            {
                continue;
            }

            if (segment is "." or "..")
            {
                throw new TraconException($"The path '{path}' is not allowed: a path in the workspace must not contain '.' or '..'.");
            }

            if (segment.Any(char.IsControl))
            {
                throw new TraconException($"The path '{path}' is not allowed: a path in the workspace must not contain control characters.");
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }
}
