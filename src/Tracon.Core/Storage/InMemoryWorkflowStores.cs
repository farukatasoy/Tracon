using System.Collections.Concurrent;
using System.Text.Json;

namespace Tracon;

/// <summary>Stores workflow definitions, and every version of them, in process memory.</summary>
/// <remarks>
/// Its behavior contract matches <c>SqlWorkflowDefinitionStore</c> and is
/// protected by shared contract tests. A deleted definition keeps its history, so a
/// version number is never given to two different contents.
/// </remarks>
internal sealed class InMemoryWorkflowDefinitionStore : IWorkflowDefinitionStore
{
    private readonly ConcurrentDictionary<WorkflowKey, WorkflowHistory> _workflows = new();

    /// <inheritdoc />
    public ValueTask<WorkflowDefinition?> GetAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();

        return new ValueTask<WorkflowDefinition?>(
            _workflows.TryGetValue(new WorkflowKey(tenantId, name), out var history) ? history.Current() : null);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<WorkflowDefinition>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        cancellationToken.ThrowIfCancellationRequested();

        var definitions = _workflows
            .Where(pair => string.Equals(pair.Key.TenantId, tenantId, StringComparison.Ordinal))
            .Select(static pair => pair.Value.Current())
            .OfType<WorkflowDefinition>()
            .OrderBy(static definition => definition.Name, StringComparer.Ordinal)
            .ToArray();

        return new ValueTask<IReadOnlyList<WorkflowDefinition>>(definitions);
    }

    /// <inheritdoc />
    public ValueTask<WorkflowDefinition> SaveAsync(
        string tenantId,
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();

        return new ValueTask<WorkflowDefinition>(
            HistoryOf(tenantId, definition.Name).Append(definition, tenantId));
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();

        return new ValueTask<bool>(
            _workflows.TryGetValue(new WorkflowKey(tenantId, name), out var history) && history.Delete());
    }

    /// <inheritdoc />
    public ValueTask<WorkflowDefinition?> GetVersionAsync(
        string tenantId,
        string name,
        int version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();

        return new ValueTask<WorkflowDefinition?>(
            _workflows.TryGetValue(new WorkflowKey(tenantId, name), out var history) ? history.Find(version) : null);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<WorkflowDefinition>> ListVersionsAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();

        return new ValueTask<IReadOnlyList<WorkflowDefinition>>(
            _workflows.TryGetValue(new WorkflowKey(tenantId, name), out var history) ? history.Newest() : []);
    }

    /// <inheritdoc />
    public ValueTask<WorkflowDefinition> RollbackAsync(
        string tenantId,
        string name,
        int version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_workflows.TryGetValue(new WorkflowKey(tenantId, name), out var history))
        {
            throw MissingVersion(name, version);
        }

        return new ValueTask<WorkflowDefinition>(
            history.AppendCopyOf(version, tenantId) ?? throw MissingVersion(name, version));
    }

    private WorkflowHistory HistoryOf(string tenantId, string name)
        => _workflows.GetOrAdd(new WorkflowKey(tenantId, name), static _ => new WorkflowHistory());

    private static TraconException MissingVersion(string name, int version)
        => new($"Version {version} of the '{name}' workflow definition was not found.");

    private readonly record struct WorkflowKey(string TenantId, string Name);

    /// <summary>
    /// Every version one name has ever had, oldest first. Kept after a delete: the last entry
    /// is then not current, but its number is still taken.
    /// </summary>
    private sealed class WorkflowHistory
    {
        private readonly List<WorkflowDefinition> _versions = [];
        private bool _exists;

        public WorkflowDefinition? Current()
        {
            lock (_versions)
            {
                return _exists ? _versions[^1] : null;
            }
        }

        public WorkflowDefinition? Find(int version)
        {
            lock (_versions)
            {
                // Versions are numbered from 1 and never skip, so the number is the position.
                return version >= 1 && version <= _versions.Count ? _versions[version - 1] : null;
            }
        }

        public IReadOnlyList<WorkflowDefinition> Newest()
        {
            lock (_versions)
            {
                return [.. Enumerable.Reverse(_versions)];
            }
        }

        public WorkflowDefinition Append(WorkflowDefinition definition, string tenantId)
        {
            lock (_versions)
            {
                return Add(definition, tenantId);
            }
        }

        public WorkflowDefinition? AppendCopyOf(int version, string tenantId)
        {
            lock (_versions)
            {
                return version >= 1 && version <= _versions.Count ? Add(_versions[version - 1], tenantId) : null;
            }
        }

        public bool Delete()
        {
            lock (_versions)
            {
                var deleted = _exists;
                _exists = false;

                return deleted;
            }
        }

        // The caller holds the lock.
        private WorkflowDefinition Add(WorkflowDefinition definition, string tenantId)
        {
            var saved = definition with
            {
                TenantId = tenantId,
                Version = _versions.Count + 1,
                UpdatedAt = DateTimeOffset.UtcNow,
            };

            _versions.Add(saved);
            _exists = true;

            return saved;
        }
    }
}

/// <summary>Stores workflow checkpoints in process memory.</summary>
/// <remarks>
/// <para>
/// In-memory checkpoints are limited to the process lifetime. This is a
/// deliberate constraint: durable resumption requires <c>UsePostgreSql()</c>,
/// and hiding that would cause a "checkpoint disappeared" surprise after an
/// application restart.
/// </para>
/// <para>
/// To prevent unbounded growth, <see cref="MaxCheckpointsPerSession"/> limits
/// checkpoints held per session and discards the oldest one.
/// </para>
/// </remarks>
internal sealed class InMemoryWorkflowCheckpointStore : IWorkflowCheckpointStore
{
    /// <summary>Gets the maximum checkpoints held in memory for a session.</summary>
    public const int MaxCheckpointsPerSession = 50;

    private readonly ConcurrentDictionary<SessionKey, List<WorkflowCheckpointRecord>> _checkpoints = new();

    /// <inheritdoc />
    public ValueTask<WorkflowCheckpointRecord> CreateAsync(
        WorkflowCheckpointRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        var list = _checkpoints.GetOrAdd(new SessionKey(record.TenantId, record.SessionId), static _ => []);

        lock (list)
        {
            list.Add(record);

            if (list.Count > MaxCheckpointsPerSession)
            {
                list.RemoveAt(0);
            }
        }

        return new ValueTask<WorkflowCheckpointRecord>(record);
    }

    /// <inheritdoc />
    public ValueTask<JsonElement?> ReadAsync(
        string tenantId,
        string sessionId,
        string checkpointId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);
        cancellationToken.ThrowIfCancellationRequested();

        cancellationToken.ThrowIfCancellationRequested();

        if (!_checkpoints.TryGetValue(new SessionKey(tenantId, sessionId), out var list))
        {
            return new ValueTask<JsonElement?>((JsonElement?)null);
        }

        lock (list)
        {
            foreach (var record in list)
            {
                if (string.Equals(record.CheckpointId, checkpointId, StringComparison.Ordinal))
                {
                    return new ValueTask<JsonElement?>(record.State);
                }
            }
        }

        return new ValueTask<JsonElement?>((JsonElement?)null);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<WorkflowCheckpointRecord>> ListAsync(
        string tenantId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_checkpoints.TryGetValue(new SessionKey(tenantId, sessionId), out var list))
        {
            return new ValueTask<IReadOnlyList<WorkflowCheckpointRecord>>([]);
        }

        lock (list)
        {
            return new ValueTask<IReadOnlyList<WorkflowCheckpointRecord>>(ToMetadata(list));
        }
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<WorkflowCheckpointRecord>> ListByRunAsync(
        string tenantId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        cancellationToken.ThrowIfCancellationRequested();

        var matches = new List<WorkflowCheckpointRecord>();

        foreach (var pair in _checkpoints)
        {
            if (!string.Equals(pair.Key.TenantId, tenantId, StringComparison.Ordinal))
            {
                continue;
            }

            lock (pair.Value)
            {
                matches.AddRange(pair.Value.Where(record => record.RunId == runId));
            }
        }

        matches.Sort(static (left, right) => left.CreatedAt.CompareTo(right.CreatedAt));

        return new ValueTask<IReadOnlyList<WorkflowCheckpointRecord>>(ToMetadata(matches));
    }

    /// <inheritdoc />
    public ValueTask<int> DeleteAsync(
        string tenantId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        cancellationToken.ThrowIfCancellationRequested();

        return new ValueTask<int>(
            _checkpoints.TryRemove(new SessionKey(tenantId, sessionId), out var removed) ? removed.Count : 0);
    }

    /// <summary>
    /// Omits the state payload from the list. The PostgreSQL implementation also
    /// returns metadata, and a difference between the stores would fail contract tests.
    /// </summary>
    private static List<WorkflowCheckpointRecord> ToMetadata(IEnumerable<WorkflowCheckpointRecord> records)
        => [.. records.Select(static record => record with { State = WorkflowCheckpointState.Omitted })];

    private readonly record struct SessionKey(string TenantId, string SessionId);
}
