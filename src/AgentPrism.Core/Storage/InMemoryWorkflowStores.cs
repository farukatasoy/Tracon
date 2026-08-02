using System.Collections.Concurrent;
using System.Text.Json;

namespace AgentPrism;

/// <summary>Workflow tanimlarini surec belleginde tutan depo.</summary>
/// <remarks>
/// Davranis sozlesmesi <c>PostgresWorkflowDefinitionStore</c> ile aynidir ve
/// ortak sozlesme testleriyle korunur.
/// </remarks>
public sealed class InMemoryWorkflowDefinitionStore : IWorkflowDefinitionStore
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

        _workflows.TryGetValue(new WorkflowKey(tenantId, name), out var definition);

        return new ValueTask<WorkflowDefinition?>(definition);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<WorkflowDefinition>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

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

/// <summary>Workflow kontrol noktalarini surec belleginde tutan depo.</summary>
/// <remarks>
/// <para>
/// Bellek ici kurulumda kontrol noktalari surec omruyle sinirlidir. Bu bilincli
/// bir kisittir: kalici sürdürme <c>UsePostgreSql()</c> gerektirir ve bunu
/// gizlemek, yeniden baslatilan bir uygulamada "checkpoint kayboldu" surprizi
/// uretirdi.
/// </para>
/// <para>
/// Sinirsiz buyumeyi onlemek icin oturum basina tutulan nokta sayisi
/// <see cref="MaxCheckpointsPerSession"/> ile sinirlidir; en eski nokta dusurulur.
/// </para>
/// </remarks>
public sealed class InMemoryWorkflowCheckpointStore : IWorkflowCheckpointStore
{
    /// <summary>Bir oturum icin bellekte tutulan en fazla kontrol noktasi sayisi.</summary>
    public const int MaxCheckpointsPerSession = 50;

    private readonly ConcurrentDictionary<SessionKey, List<WorkflowCheckpointRecord>> _checkpoints = new();

    /// <inheritdoc />
    public ValueTask<WorkflowCheckpointRecord> CreateAsync(
        WorkflowCheckpointRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

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

        return new ValueTask<int>(
            _checkpoints.TryRemove(new SessionKey(tenantId, sessionId), out var removed) ? removed.Count : 0);
    }

    /// <summary>
    /// Durum yukunu listeden dusurur; PostgreSQL uygulamasi da ustveri doner ve
    /// iki depo arasindaki fark sozlesme testinde hata olurdu.
    /// </summary>
    private static List<WorkflowCheckpointRecord> ToMetadata(IEnumerable<WorkflowCheckpointRecord> records)
        => [.. records.Select(static record => record with { State = WorkflowCheckpointState.Omitted })];

    private readonly record struct SessionKey(string TenantId, string SessionId);
}
