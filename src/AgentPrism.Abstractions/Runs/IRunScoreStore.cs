namespace AgentPrism;

/// <summary>
/// The store for run and message scores (<see cref="RunScore"/>).
/// </summary>
/// <remarks>
/// <para>
/// Not added as a member to <see cref="IRunStore"/>: a score follows a
/// different lifecycle (written rarely, whereas <see cref="IRunStore"/> is on
/// the hot path written on every run), and a separate interface fits better
/// with the rule that every extension point must be replaceable.
/// </para>
/// <para>
/// <strong>Tenant behavior — EXPECTED tenant, uniformly.</strong> Every
/// member is scoped by an explicit tenant: <see cref="UpsertAsync"/> reads it
/// from <see cref="RunScore.TenantId"/>, <see cref="ListAsync"/> and
/// <see cref="DeleteAsync"/> take it as a parameter. The ambient tenant is
/// never consulted.
/// </para>
/// </remarks>
public interface IRunScoreStore
{
    /// <summary>Adds or updates a score.</summary>
    /// <param name="score">
    /// The score. If <see cref="RunScore.Id"/> is empty (<see cref="Guid.Empty"/>), a
    /// new identifier is generated.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The score written.</returns>
    /// <remarks>
    /// When the same author (<see cref="RunScore.Author"/>) scores the same
    /// target (a run or a message) a second time, the row is
    /// <strong>updated</strong>, not opened as a new row. If
    /// <see cref="RunScore.Author"/> is empty, this rule does not apply; every
    /// call writes a new row.
    /// </remarks>
    ValueTask<RunScore> UpsertAsync(RunScore score, CancellationToken cancellationToken = default);

    /// <summary>Lists all of a run's scores.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="runId">The run identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The scores. No order is guaranteed.</returns>
    ValueTask<IReadOnlyList<RunScore>> ListAsync(
        string tenantId,
        Guid runId,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a score.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="scoreId">The score identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if deleted; <see langword="false"/> if no such score exists.</returns>
    ValueTask<bool> DeleteAsync(
        string tenantId,
        Guid scoreId,
        CancellationToken cancellationToken = default);
}
