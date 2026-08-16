using System.Data.Common;

namespace AgentPrism;

/// <summary>Persists run and message scores.</summary>
/// <remarks>
/// The behavior contract is identical to <see cref="InMemoryRunScoreStore"/>;
/// it is guarded by the shared contract tests.
/// </remarks>
internal sealed class SqlRunScoreStore : IRunScoreStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Creates a new score store.</summary>
    /// <param name="context">The store context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> <see langword="null"/> ise.</exception>
    public SqlRunScoreStore(SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask<RunScore> UpsertAsync(RunScore score, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(score);

        var command = _context.CreateCommand(_sql.UpsertRunScore);
        DbHelpers.Add(command, "id", score.Id == Guid.Empty ? AgentPrismId.NewId() : score.Id);
        DbHelpers.Add(command, "tenant_id", score.TenantId);
        DbHelpers.Add(command, "run_id", score.RunId);
        Dialect.AddText(command, "message_id", score.MessageId);
        DbHelpers.Add(command, "kind", (short)score.Kind);
        DbHelpers.Add(command, "value", score.Value);
        Dialect.AddText(command, "comment", score.Comment);
        DbHelpers.Add(command, "source", score.Source);
        Dialect.AddText(command, "author", score.Author);
        Dialect.AddTimestamp(command, "created_at", score.CreatedAt);

        var saved = await DbHelpers.ReadSingleAsync(command, Read, cancellationToken).ConfigureAwait(false);

        return saved ?? score;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<RunScore>> ListAsync(
        string tenantId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = _context.CreateCommand(_sql.SelectRunScores);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "run_id", runId);

        return await DbHelpers.ReadListAsync(command, Read, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(
        string tenantId,
        Guid scoreId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = _context.CreateCommand(_sql.DeleteRunScore);
        DbHelpers.Add(command, "id", scoreId);
        DbHelpers.Add(command, "tenant_id", tenantId);

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    private static RunScore Read(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            RunId = reader.GetGuid(2),
            MessageId = DbHelpers.GetNullableString(reader, 3),
            Kind = (RunScoreKind)reader.GetInt16(4),
            Value = reader.GetInt32(5),
            Comment = DbHelpers.GetNullableString(reader, 6),
            Source = reader.GetString(7),
            Author = DbHelpers.GetNullableString(reader, 8),
            CreatedAt = DbHelpers.GetTimestamp(reader, 9),
        };
}
