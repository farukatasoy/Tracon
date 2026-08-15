using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;

namespace AgentPrism;

/// <summary>
/// Adapts the Microsoft Agent Framework checkpoint store to AgentPrism's
/// <see cref="IWorkflowCheckpointStore"/> contract.
/// </summary>
/// <remarks>
/// <para>
/// The adapter lives <strong>in this package</strong>. Reason: the checkpoint
/// store is in <c>AgentPrism.PostgreSql</c>, and that package must not see the
/// workflow engine's types. The contract is expressed with
/// <see cref="JsonElement"/>; the conversion to the MAF type happens only here.
/// </para>
/// <para>
/// 🚨 <strong>AgentPrism generates the checkpoint id.</strong> MAF expects a
/// <see cref="CheckpointInfo"/> and its content is our own decision; using a
/// time-ordered UUID moves the list's natural ordering into the id itself.
/// </para>
/// <para>
/// The tenant and run id are read from the <em>ambient scope</em>: MAF's write
/// call carries no context parameter. The same solution was used for
/// <c>PostgresAgentFileStore</c> in phase 14 (K-114).
/// </para>
/// </remarks>
internal sealed class AgentPrismCheckpointStore : ICheckpointStore<JsonElement>
{
    private readonly IWorkflowCheckpointStore _store;
    private readonly ITenantContext _tenantContext;

    /// <summary>Creates a new adapter.</summary>
    /// <param name="store">The AgentPrism checkpoint store.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public AgentPrismCheckpointStore(IWorkflowCheckpointStore store, ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _store = store;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async ValueTask<CheckpointInfo> CreateCheckpointAsync(
        string sessionId,
        JsonElement value,
        CheckpointInfo? parent = null)
    {
        var scope = AgentPrismRunContext.Current;
        var checkpointId = AgentPrismId.NewId().ToString("n", System.Globalization.CultureInfo.InvariantCulture);

        await _store.CreateAsync(
            new WorkflowCheckpointRecord
            {
                Id = AgentPrismId.NewId(),
                TenantId = scope?.TenantId ?? _tenantContext.TenantId,
                SessionId = sessionId,
                CheckpointId = checkpointId,
                ParentCheckpointId = parent?.CheckpointId,
                RunId = scope?.RunId,
                CreatedAt = DateTimeOffset.UtcNow,
                State = value,
            },
            CancellationToken.None).ConfigureAwait(false);

        return new CheckpointInfo(sessionId, checkpointId);
    }

    /// <inheritdoc />
    public async ValueTask<JsonElement> RetrieveCheckpointAsync(string sessionId, CheckpointInfo key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var tenantId = AgentPrismRunContext.Current?.TenantId ?? _tenantContext.TenantId;

        var state = await _store
            .ReadAsync(tenantId, sessionId, key.CheckpointId, CancellationToken.None)
            .ConfigureAwait(false);

        // When the tenant does not match, the store returns null and this
        // surfaces as "not found". The message does NOT mention the tenant:
        // even the knowledge that another tenant's checkpoint exists must not
        // leak.
        return state ?? throw new AgentPrismException(
            $"Checkpoint '{key.CheckpointId}' was not found.");
    }

    /// <inheritdoc />
    public async ValueTask<IEnumerable<CheckpointInfo>> RetrieveIndexAsync(
        string sessionId,
        CheckpointInfo? withParent = null)
    {
        var tenantId = AgentPrismRunContext.Current?.TenantId ?? _tenantContext.TenantId;

        var records = await _store.ListAsync(tenantId, sessionId, CancellationToken.None).ConfigureAwait(false);

        // The parent filter is applied in memory: a session's checkpoints are
        // bounded by MaxSuperSteps, and opening a separate query path would
        // mean repeating the same column filter in both store implementations.
        var filtered = withParent is null
            ? records
            : [.. records.Where(record =>
                string.Equals(record.ParentCheckpointId, withParent.CheckpointId, StringComparison.Ordinal))];

        return [.. filtered.Select(record => new CheckpointInfo(record.SessionId, record.CheckpointId))];
    }
}
