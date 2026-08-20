using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// The store for workflow checkpoints.
/// </summary>
/// <remarks>
/// <para>
/// The contract deliberately <strong>carries no</strong> Microsoft Agent
/// Framework type: the state is expressed as a <see cref="JsonElement"/>.
/// This way, <c>AgentPrism.PostgreSql</c> stores checkpoints without binding
/// to the workflow engine's 130-type API surface. Adapting to MAF's
/// <c>ICheckpointStore&lt;JsonElement&gt;</c> interface happens inside
/// <c>AgentPrism.Workflows</c>.
/// </para>
/// <para>
/// <strong>Every read is scoped by the tenant filter.</strong> A
/// checkpoint carries the entire execution state; another tenant's
/// checkpoint returns <em>not found</em> — not even "unauthorized" is said, its existence is not leaked.
/// </para>
/// </remarks>
public interface IWorkflowCheckpointStore
{
    /// <summary>Writes a new checkpoint.</summary>
    /// <param name="record">The record to write.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The written record.</returns>
    ValueTask<WorkflowCheckpointRecord> CreateAsync(
        WorkflowCheckpointRecord record,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a single checkpoint's state.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="sessionId">The execution session identifier.</param>
    /// <param name="checkpointId">The checkpoint identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The opaque state; <see langword="null"/> if the record does not exist or belongs to another tenant.</returns>
    ValueTask<JsonElement?> ReadAsync(
        string tenantId,
        string sessionId,
        string checkpointId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists a session's checkpoints, oldest to newest. The state payload is
    /// <strong>not read</strong>; only metadata is returned.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="sessionId">The execution session identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The records.</returns>
    ValueTask<IReadOnlyList<WorkflowCheckpointRecord>> ListAsync(
        string tenantId,
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the checkpoints a single run produced, oldest to newest. The
    /// state payload is <strong>not read</strong>.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="runId">The run identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The records.</returns>
    /// <remarks>
    /// Separate from the session-based list: a session can carry multiple
    /// runs (resuming opens a new run each time), and the UI asks "where can
    /// this run be resumed from."
    /// </remarks>
    ValueTask<IReadOnlyList<WorkflowCheckpointRecord>> ListByRunAsync(
        string tenantId,
        Guid runId,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes all of a session's checkpoints.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="sessionId">The execution session identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of records deleted.</returns>
    ValueTask<int> DeleteAsync(
        string tenantId,
        string sessionId,
        CancellationToken cancellationToken = default);
}
