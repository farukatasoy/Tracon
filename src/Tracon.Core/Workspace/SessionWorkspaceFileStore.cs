using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// The file store the file tools of a session workspace use: a private directory of one session of
/// one agent, with a size limit, over the agent file store the host registered.
/// </summary>
/// <remarks>
/// <para>
/// The compiled agent is cached per tenant and shared by every run, so the store holds only what is
/// fixed at compile time (tenant and agent) and reads the session from the run that is calling
/// (<see cref="TraconRunContext"/>) every time. A call outside a session fails: the provider offers no
/// tool to a run without a session, so only a bug reaches it.
/// </para>
/// <para>
/// The agent calls the store through the Microsoft Agent Framework tools, which clean the path. The store cleans it
/// again (<see cref="SessionWorkspacePaths.Relative"/>) and keeps the result under the root of the
/// workspace; a path that would leave it is refused, not rewritten.
/// </para>
/// <para>
/// A write is refused when the file, the files of the session, or the workspaces of the tenant would pass the
/// limit in <see cref="TraconWorkspaceOptions"/>. A file counts its content and a fixed amount for its row
/// (<c>FileOverheadBytes</c>), so empty files cannot pile up for free. The check reads the size at the moment of the
/// write and does not lock, so two writes that run together can pass a limit by one file (the limits are soft).
/// </para>
/// </remarks>
// MAAI001: AgentFileStore is marked "evaluation purposes only" - the rationale is the same as in
// AgentDefinitionCompiler.Memory.cs.
#pragma warning disable MAAI001
internal sealed class SessionWorkspaceFileStore : AgentFileStore
{
    private readonly AgentFileStore _inner;
    private readonly IAgentFileTree _tree;
    private readonly string _tenantId;
    private readonly string _agentName;
    private readonly IOptionsMonitor<TraconWorkspaceOptions> _options;

    /// <summary>Creates the store for one compiled agent.</summary>
    /// <param name="inner">The agent file store the host registered.</param>
    /// <param name="tree">The directory operations of <paramref name="inner"/>.</param>
    /// <param name="tenantId">The tenant the agent is compiled for.</param>
    /// <param name="agentName">The agent's name.</param>
    /// <param name="options">The limits.</param>
    public SessionWorkspaceFileStore(
        AgentFileStore inner,
        IAgentFileTree tree,
        string tenantId,
        string agentName,
        IOptionsMonitor<TraconWorkspaceOptions> options)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        ArgumentException.ThrowIfNullOrEmpty(agentName);
        ArgumentNullException.ThrowIfNull(options);

        _inner = inner;
        _tree = tree;
        _tenantId = AmbientTenantScope.Normalize(tenantId);
        _agentName = agentName;
        _options = options;
    }

    /// <summary>
    /// Gets whether the run that is calling has a workspace here: it has a session with a record, and
    /// it belongs to the tenant the agent was compiled for.
    /// </summary>
    internal bool HasWorkspace => TryGetSessionId(out _);

    /// <inheritdoc />
    public override Task<string?> ReadAsync(string path, CancellationToken cancellationToken = default)
        => _inner.ReadAsync(Rewrite(path), cancellationToken);

    /// <inheritdoc />
    public override async Task WriteAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var target = Rewrite(path);
        var limits = _options.CurrentValue;
        var size = Encoding.UTF8.GetByteCount(content);

        if (size > limits.MaxFileBytes)
        {
            throw new TraconException(
                $"Workspace quota reached: a file may hold at most {limits.MaxFileBytes} bytes, and this one holds {size}. Write less, or split it into smaller files.");
        }

        // A write that replaces a file adds only the difference; a new file adds its content and the cost of its row.
        var replaced = await _inner.ReadAsync(target, cancellationToken).ConfigureAwait(false);
        var overhead = limits.FileOverheadBytes;
        var added = replaced is null ? size + overhead : size - Encoding.UTF8.GetByteCount(replaced);

        if (added > 0)
        {
            var session = await _tree.MeasureAsync(_tenantId, SessionRoot(), cancellationToken).ConfigureAwait(false);

            if (session.Bytes + (session.Files * overhead) + added > limits.MaxSessionBytes)
            {
                throw new TraconException(
                    $"Workspace quota reached: the files of this session may hold at most {limits.MaxSessionBytes} bytes, " +
                    $"and each file counts {overhead} bytes more than its content. Delete files you no longer need, or write less.");
            }

            var tenant = await _tree.MeasureAsync(_tenantId, SessionWorkspacePaths.TenantRoot(_tenantId), cancellationToken).ConfigureAwait(false);

            if (tenant.Bytes + (tenant.Files * overhead) + added > limits.MaxTenantBytes)
            {
                throw new TraconException("Workspace quota reached: the workspaces of this tenant are full. Delete files you no longer need.");
            }
        }

        await _inner.WriteAsync(target, content, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override Task<bool> DeleteAsync(string path, CancellationToken cancellationToken = default)
        => _inner.DeleteAsync(Rewrite(path), cancellationToken);

    /// <inheritdoc />
    public override Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default)
        => _inner.FileExistsAsync(Rewrite(path), cancellationToken);

    /// <inheritdoc />
    public override Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
        => _inner.CreateDirectoryAsync(Rewrite(path), cancellationToken);

    /// <inheritdoc />
    public override Task<IReadOnlyList<FileStoreEntry>> ListChildrenAsync(string directory, CancellationToken cancellationToken = default)
        => _inner.ListChildrenAsync(Rewrite(directory), cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// The Microsoft Agent Framework tool puts the searched directory in front of each name it gets back, so a
    /// name must be relative to that directory. The in-memory store answers that way; the SQL store answers with
    /// the whole stored path (leading <c>/</c>), which would show the tenant and the session key to the model.
    /// </remarks>
    public override async Task<IReadOnlyList<FileSearchResult>> SearchAsync(
        string directory,
        string regexPattern,
        string? globPattern = null,
        bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        var searched = Rewrite(directory);
        var matches = await _inner.SearchAsync(searched, regexPattern, globPattern, recursive, cancellationToken).ConfigureAwait(false);

        return matches.Select(match => new FileSearchResult
        {
            FileName = RelativeToDirectory(match.FileName, searched),
            MatchingLines = match.MatchingLines,
            Snippet = match.Snippet,
        }).ToList();
    }

    private static string RelativeToDirectory(string fileName, string directory)
    {
        if (!fileName.StartsWith('/'))
        {
            return fileName;
        }

        var prefix = "/" + directory + "/";

        return fileName.StartsWith(prefix, StringComparison.Ordinal) ? fileName[prefix.Length..] : fileName.TrimStart('/');
    }

    private bool TryGetSessionId(out string sessionId)
    {
        sessionId = string.Empty;

        if (TraconRunContext.Current is not { SessionId: { Length: > 0 } id, SessionHasNoRecord: false } run)
        {
            return false;
        }

        // The compiled agent is tenant-specific and is cached per tenant; this guards a compiler that
        // was handed to a second tenant by hand.
        if (run.TenantId is { } runTenant && !string.Equals(AmbientTenantScope.Normalize(runTenant), _tenantId, StringComparison.Ordinal))
        {
            return false;
        }

        sessionId = id;

        return true;
    }

    private string SessionRoot()
        => TryGetSessionId(out var sessionId)
            ? SessionWorkspacePaths.SessionRoot(_tenantId, sessionId)
            : throw new TraconException("This run has no session that is kept, so it has no workspace.");

    /// <summary>Turns a path of the workspace into the path in the host's store.</summary>
    /// <exception cref="TraconException">There is no workspace for this run, the path leaves the workspace, or the path is too long.</exception>
    private string Rewrite(string path)
    {
        var root = TryGetSessionId(out var sessionId)
            ? SessionWorkspacePaths.AgentRoot(_tenantId, sessionId, _agentName)
            : throw new TraconException("This run has no session that is kept, so it has no workspace.");

        var relative = SessionWorkspacePaths.Relative(path);
        var target = relative.Length == 0 ? root : root + "/" + relative;

        // The stored path has a leading "/".
        if (target.Length + 1 > SessionWorkspacePaths.MaxPathLength)
        {
            var room = SessionWorkspacePaths.MaxPathLength - 2 - root.Length;

            throw new TraconException($"The path is too long: a path in this workspace may hold at most {room} characters.");
        }

        return target;
    }
}
#pragma warning restore MAAI001
