using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

/// <summary>
/// Counts, exports and deletes the files of the session workspaces of sessions.
/// </summary>
/// <remarks>
/// <para>
/// The files live in the agent file store, in one directory per session
/// (<see cref="SessionWorkspacePaths.SessionRoot"/>) that holds the workspace of every agent that session
/// used. The directory is found from the tenant and the session id alone, so a session goes
/// without the agent being known: through <c>DELETE /api/sessions/{id}</c>, an erasure of its
/// data subject, or its retention period.
/// </para>
/// <para>
/// It takes the store from the container when it first has work, not when it is built: it is
/// constructed with the SQL stores, before a host that registers its own store late has done so.
/// Every path names the tenant as well as the session. A session id is unique inside one tenant
/// only.
/// </para>
/// </remarks>
// MAAI001: AgentFileStore is marked "evaluation purposes only" - the rationale is the same as in
// AgentDefinitionCompiler.Memory.cs.
#pragma warning disable MAAI001
internal sealed class SessionWorkspaceEraser(IServiceProvider services) : ISubjectContentEraser
{
    private readonly Lazy<IAgentFileTree> _tree = new(() =>
    {
        var store = services.GetRequiredService<AgentFileStore>();

        return store as IAgentFileTree ?? new GenericAgentFileTree(store);
    });

    /// <inheritdoc />
    public string Target => "session_workspace";

    /// <inheritdoc />
    public async ValueTask<int> CountAsync(string tenantId, IReadOnlyList<string> sessionIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionIds);
        var count = 0;

        foreach (var sessionId in sessionIds.Where(static id => id.Length > 0))
        {
            var usage = await Tree.MeasureAsync(Canonical(tenantId), SessionWorkspacePaths.SessionRoot(tenantId, sessionId), cancellationToken).ConfigureAwait(false);
            count += usage.Files;
        }

        return count;
    }

    /// <inheritdoc />
    public async ValueTask<int> EraseAsync(string tenantId, IReadOnlyList<string> sessionIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionIds);
        var deleted = 0;

        foreach (var sessionId in sessionIds.Where(static id => id.Length > 0))
        {
            deleted += await Tree.DeleteAsync(Canonical(tenantId), SessionWorkspacePaths.SessionRoot(tenantId, sessionId), cancellationToken).ConfigureAwait(false);
        }

        return deleted;
    }

    /// <inheritdoc />
    public async ValueTask WriteExportAsync(
        string tenantId,
        IReadOnlyList<string> sessionIds,
        Utf8JsonWriter writer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionIds);
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStartArray(Target);

        foreach (var sessionId in sessionIds.Where(static id => id.Length > 0))
        {
            var files = await Tree.ReadAsync(Canonical(tenantId), SessionWorkspacePaths.SessionRoot(tenantId, sessionId), cancellationToken).ConfigureAwait(false);

            foreach (var file in files)
            {
                // The path below the session directory is "{agent}/{path}".
                var slash = file.Path.IndexOf('/', StringComparison.Ordinal);

                writer.WriteStartObject();
                writer.WriteString("sessionId", sessionId);
                writer.WriteString("agent", slash < 0 ? file.Path : file.Path[..slash]);
                writer.WriteString("path", slash < 0 ? string.Empty : file.Path[(slash + 1)..]);
                writer.WriteString("content", file.Content);
                writer.WriteEndObject();
            }
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// The tenant in the form the files were written in: the store writes <c>AmbientTenantScope.Normalize</c>,
    /// and a tenant context that does not normalize must still find them.
    /// </summary>
    private static string Canonical(string tenantId) => AmbientTenantScope.Normalize(tenantId);

    private IAgentFileTree Tree => _tree.Value;
}
#pragma warning restore MAAI001
