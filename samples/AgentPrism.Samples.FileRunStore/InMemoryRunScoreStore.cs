namespace AgentPrism.Samples.FileRunStore;

/// <summary>
/// A non-persistent <see cref="IRunScoreStore"/>, used as
/// <see cref="JsonFileRunStore"/>'s default when the caller supplies none.
/// </summary>
/// <remarks>
/// Scores are written far less often than run records and are not the focus
/// of this sample; a real deployment would give <see cref="JsonFileRunStore"/>
/// its own persistent <see cref="IRunScoreStore"/> instead.
/// </remarks>
internal sealed class InMemoryRunScoreStore : IRunScoreStore
{
    private readonly Lock _gate = new();
    private readonly List<RunScore> _scores = [];

    /// <inheritdoc />
    public ValueTask<RunScore> UpsertAsync(RunScore score, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(score);
        RunScoreRules.Validate(score);

        lock (_gate)
        {
            var id = score.Id == Guid.Empty ? Guid.NewGuid() : score.Id;

            // The same author writing the same NAME onto the same target a
            // second time updates the row instead of opening a new one; a
            // different name, or an author-less score, opens a new row.
            var existingIndex = score.Author is { Length: > 0 }
                ? _scores.FindIndex(candidate =>
                    candidate.RunId == score.RunId
                    && string.Equals(candidate.MessageId, score.MessageId, StringComparison.Ordinal)
                    && string.Equals(candidate.Author, score.Author, StringComparison.Ordinal)
                    && string.Equals(candidate.Name, score.Name, StringComparison.Ordinal))
                : -1;

            var stored = score with { Id = id };

            if (existingIndex >= 0)
            {
                stored = stored with { Id = _scores[existingIndex].Id };
                _scores[existingIndex] = stored;
            }
            else
            {
                _scores.Add(stored);
            }

            return new ValueTask<RunScore>(stored);
        }
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<RunScore>> ListAsync(string tenantId, Guid runId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<RunScore> matches =
            [
                .. _scores.Where(score =>
                    score.RunId == runId
                    && string.Equals(score.TenantId, tenantId, StringComparison.Ordinal)),
            ];

            return new ValueTask<IReadOnlyList<RunScore>>(matches);
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteAsync(string tenantId, Guid scoreId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var index = _scores.FindIndex(score =>
                score.Id == scoreId && string.Equals(score.TenantId, tenantId, StringComparison.Ordinal));

            if (index < 0)
            {
                return new ValueTask<bool>(false);
            }

            _scores.RemoveAt(index);
            return new ValueTask<bool>(true);
        }
    }
}
