namespace AgentPrism;

// MAAI001: Microsoft.Agents.AI.AgentFileStore is marked "evaluation purposes
// only". The rationale for suppressing is the same as in AgentDefinitionCompiler.
#pragma warning disable MAAI001

/// <summary>
/// A wrapper that behaves like a private subtree, isolated per tenant AND per
/// agent, over a shared <see cref="Microsoft.Agents.AI.AgentFileStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Microsoft.Agents.AI.AgentFileStore"/> is, by default, a SINGLE
/// store shared process-wide (<c>InMemoryAgentFileStore</c>, <c>TryAddSingleton</c>).
/// Without this wrapper, a recursive search performed from the root
/// <c>"/"</c> directory by one tenant's agent with <c>EnableTextSearch</c> on
/// could see files another tenant wrote via <c>EnableFileMemory</c> - a
/// breach of tenant isolation.
/// </para>
/// <para>
/// 🚨 The tenant segment alone is NOT enough (defect F-105). Two agents of the
/// same tenant shared one subtree, so a private note written by one agent was
/// found by another agent's text search. The prefix therefore carries the agent
/// name as well: <c>/{tenantId}/{agentName}/...</c>. The agent name is known at
/// COMPILE time and is already part of the <c>CompiledAgentCache</c> key, so
/// this needs no ambient state - unlike a per-session boundary, which would.
/// </para>
/// <para>
/// Files stay shared between the SESSIONS of one agent, and that is deliberate:
/// file memory is an agent-level memory, and isolating it per session would
/// erase what the agent remembers at the start of every conversation.
/// </para>
/// <para>
/// Every incoming path is routed to the inner store with the prefix above; the
/// same prefix is also stripped from outgoing path/name fields. The result: on
/// the wrapped side, both the write path (<c>FileMemoryProvider</c>) and the
/// read path (<c>TextSearchProvider</c>) of
/// <see cref="AgentDefinitionCompiler"/> operate with <c>"/"</c> as if it were
/// their own private root directory; the real prefix in the inner store never
/// leaks to the calling side.
/// </para>
/// </remarks>
internal sealed class TenantPrefixingAgentFileStore : Microsoft.Agents.AI.AgentFileStore
{
    private readonly Microsoft.Agents.AI.AgentFileStore _inner;
    private readonly string _prefix;

    /// <summary>Creates a new wrapper isolated to one agent of one tenant.</summary>
    /// <param name="inner">The shared store being wrapped.</param>
    /// <param name="tenantId">Identifier of the tenant being isolated.</param>
    /// <param name="agentName">Name of the agent being isolated inside that tenant.</param>
    public TenantPrefixingAgentFileStore(
        Microsoft.Agents.AI.AgentFileStore inner,
        string tenantId,
        string agentName)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        ArgumentException.ThrowIfNullOrEmpty(agentName);

        _inner = inner;
        _prefix = tenantId + "/" + agentName;
    }

    /// <inheritdoc />
    public override Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
        => _inner.CreateDirectoryAsync(Rewrite(path), cancellationToken);

    /// <inheritdoc />
    public override Task<bool> DeleteAsync(string path, CancellationToken cancellationToken = default)
        => _inner.DeleteAsync(Rewrite(path), cancellationToken);

    /// <inheritdoc />
    public override Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default)
        => _inner.FileExistsAsync(Rewrite(path), cancellationToken);

    /// <inheritdoc />
    public override async Task<IReadOnlyList<Microsoft.Agents.AI.FileStoreEntry>> ListChildrenAsync(
        string directory, CancellationToken cancellationToken = default)
    {
        var entries = await _inner.ListChildrenAsync(Rewrite(directory), cancellationToken).ConfigureAwait(false);

        return entries.Select(entry => new Microsoft.Agents.AI.FileStoreEntry(StripPrefix(entry.Name), entry.Type))
            .ToList();
    }

    /// <inheritdoc />
    public override Task<string?> ReadAsync(string path, CancellationToken cancellationToken = default)
        => _inner.ReadAsync(Rewrite(path), cancellationToken);

    /// <inheritdoc />
    public override async Task<IReadOnlyList<Microsoft.Agents.AI.FileSearchResult>> SearchAsync(
        string directory,
        string regexPattern,
        string? globPattern = null,
        bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        var matches = await _inner
            .SearchAsync(Rewrite(directory), regexPattern, globPattern, recursive, cancellationToken)
            .ConfigureAwait(false);

        return matches.Select(match => new Microsoft.Agents.AI.FileSearchResult
        {
            FileName = StripPrefix(match.FileName),
            MatchingLines = match.MatchingLines,
            Snippet = match.Snippet,
        }).ToList();
    }

    /// <inheritdoc />
    public override Task WriteAsync(string path, string content, CancellationToken cancellationToken = default)
        => _inner.WriteAsync(Rewrite(path), content, cancellationToken);

    private string Rewrite(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        // The inner store (e.g. InMemoryAgentFileStore) requires the path to be
        // RELATIVE and NOT START with '/' (NormalizeRelativePath). "" and "/"
        // represent the caller's own root directory ("me").
        if (path.Length == 0 || string.Equals(path, "/", StringComparison.Ordinal))
        {
            return _prefix;
        }

        var relative = path[0] == '/' ? path[1..] : path;

        return _prefix + "/" + relative;
    }

    private string StripPrefix(string value)
    {
        if (string.Equals(value, _prefix, StringComparison.Ordinal))
        {
            return "/";
        }

        return value.StartsWith(_prefix + "/", StringComparison.Ordinal)
            ? value[_prefix.Length..]
            : value;
    }
}
#pragma warning restore MAAI001
