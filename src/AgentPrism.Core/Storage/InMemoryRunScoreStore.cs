using System.Collections.Concurrent;

namespace AgentPrism;

/// <summary>
/// A store that keeps run and message scores in process memory.
/// </summary>
/// <remarks>
/// Its behavior contract exactly matches persistent implementations such as
/// <c>SqlRunScoreStore</c>, and shared contract tests protect it. Use
/// <c>AgentPrism.PostgreSql</c>, SQL Server, or SQLite in production.
/// </remarks>
internal sealed class InMemoryRunScoreStore : IRunScoreStore
{
    private readonly ConcurrentDictionary<Guid, RunScore> _scores = new();

    /// <inheritdoc />
    public ValueTask<RunScore> UpsertAsync(RunScore score, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(score);

        // When the same author scores the same target, run or message, a second time,
        // update the existing row. If the author is empty in an anonymous deployment,
        // this rule does not apply and each call creates a new row. This matches the
        // unique index in SQL providers. See the run_scores migration.
        if (score.Author is { Length: > 0 })
        {
            var existing = _scores.Values.FirstOrDefault(candidate => IsSameTarget(candidate, score));

            if (existing is not null)
            {
                var updated = score with { Id = existing.Id };
                _scores[existing.Id] = updated;

                return new ValueTask<RunScore>(updated);
            }
        }

        var created = score with { Id = score.Id == Guid.Empty ? AgentPrismId.NewId() : score.Id };
        _scores[created.Id] = created;

        return new ValueTask<RunScore>(created);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<RunScore>> ListAsync(
        string tenantId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        IReadOnlyList<RunScore> result =
        [
            .. _scores.Values.Where(score =>
                score.RunId == runId && string.Equals(score.TenantId, tenantId, StringComparison.Ordinal)),
        ];

        return new ValueTask<IReadOnlyList<RunScore>>(result);
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteAsync(
        string tenantId,
        Guid scoreId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        if (_scores.TryGetValue(scoreId, out var existing)
            && string.Equals(existing.TenantId, tenantId, StringComparison.Ordinal))
        {
            return new ValueTask<bool>(_scores.TryRemove(scoreId, out _));
        }

        return new ValueTask<bool>(false);
    }

    private static bool IsSameTarget(RunScore left, RunScore right)
        => string.Equals(left.TenantId, right.TenantId, StringComparison.Ordinal)
           && left.RunId == right.RunId
           && string.Equals(left.MessageId ?? string.Empty, right.MessageId ?? string.Empty, StringComparison.Ordinal)
           && string.Equals(left.Author, right.Author, StringComparison.Ordinal);
}
