using System.Collections.Concurrent;
using System.Text.Json;

namespace Tracon;

/// <summary>Stores workflow definitions in process memory.</summary>
/// <remarks>
/// Its behavior contract matches <c>PostgresWorkflowDefinitionStore</c> and is
/// protected by shared contract tests.
/// </remarks>
internal sealed class InMemoryWorkflowDefinitionStore : IWorkflowDefinitionStore
{
    private readonly ConcurrentDictionary<WorkflowKey, WorkflowDefinition> _workflows = new();

    /// <inheritdoc />
    public ValueTask<WorkflowDefinition?> GetAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();

        _workflows.TryGetValue(new WorkflowKey(tenantId, name), out var definition);

        return new ValueTask<WorkflowDefinition?>(definition);
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
            .Select(static pair => pair.Value)
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

        var saved = _workflows.AddOrUpdate(
            new WorkflowKey(tenantId, definition.Name),
            (_, value) => Stamp(value, tenantId, version: 1),
            (_, current, value) => Stamp(value, tenantId, current.Version + 1),
            definition);

        return new ValueTask<WorkflowDefinition>(saved);
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

        return new ValueTask<bool>(_workflows.TryRemove(new WorkflowKey(tenantId, name), out _));
    }

    private static WorkflowDefinition Stamp(WorkflowDefinition definition, string tenantId, int version)
        => definition with
        {
            TenantId = tenantId,
            Version = version,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    private readonly record struct WorkflowKey(string TenantId, string Name);
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
public sealed class InMemoryWorkflowCheckpointStore : IWorkflowCheckpointStore
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
