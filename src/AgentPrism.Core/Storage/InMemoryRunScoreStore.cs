using System.Collections.Concurrent;

namespace AgentPrism;

/// <summary>
/// Calistirma ve mesaj puanlarini surec bellegi icinde tutan depo.
/// </summary>
/// <remarks>
/// Davranis sozlesmesi kalici uygulamalarla (<c>SqlRunScoreStore</c>) birebir
/// aynidir ve ortak sozlesme testleriyle korunur. Uretimde
/// <c>AgentPrism.PostgreSql</c> (veya SQL Server/SQLite) kullanin.
/// </remarks>
public sealed class InMemoryRunScoreStore : IRunScoreStore
{
    private readonly ConcurrentDictionary<Guid, RunScore> _scores = new();

    /// <inheritdoc />
    public ValueTask<RunScore> UpsertAsync(RunScore score, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(score);

        // Ayni yazar ayni hedefi (calistirma veya mesaj) ikinci kez
        // puanladiginda mevcut satir GUNCELLENIR. Yazar bos ise (kimliksiz
        // kurulum) bu kural uygulanmaz -- her cagri yeni bir satir acar.
        // SQL saglayicilarindaki benzersizlik indeksiyle ayni davranistir
        // (bkz. run_scores migration'i).
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
