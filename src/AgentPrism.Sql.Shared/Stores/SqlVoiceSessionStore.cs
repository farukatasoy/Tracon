using System.Data.Common;

namespace AgentPrism;

/// <summary>Konusma baglantilarinin ozet kaydini kalici olarak saklayan depo.</summary>
/// <remarks>
/// Davranis sozlesmesi <see cref="InMemoryVoiceSessionStore"/> ile birebir
/// aynidir; tek fark bellek ici uygulamanin kayit sayisini sinirlamasidir.
/// </remarks>
internal sealed class SqlVoiceSessionStore : IVoiceSessionStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Yeni bir konusma kaydi deposu olusturur.</summary>
    /// <param name="context">Depo baglami.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> <see langword="null"/> ise.</exception>
    public SqlVoiceSessionStore(SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask SaveAsync(VoiceSessionRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var command = _context.CreateCommand(_sql.UpsertVoiceSession);
        DbHelpers.Add(command, "id", record.Id);
        DbHelpers.Add(command, "tenant_id", record.TenantId);
        DbHelpers.Add(command, "session_id", record.SessionId);
        DbHelpers.Add(command, "agent_name", record.AgentName);
        Dialect.AddTimestamp(command, "started_at", record.StartedAt);
        Dialect.AddTimestamp(command, "ended_at", record.EndedAt);
        DbHelpers.Add(command, "turns", record.Turns);
        Dialect.AddDecimal(command, "input_seconds", record.InputSeconds);
        Dialect.AddInt64(command, "output_chars", record.OutputChars);
        Dialect.AddInt16(command, "end_reason", (short?)record.EndReason);
        Dialect.AddText(command, "created_by", record.CreatedBy);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<VoiceSessionRecord>> QueryAsync(
        string tenantId,
        VoiceSessionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(query);

        var command = _context.CreateCommand(_sql.SelectVoiceSessions);
        DbHelpers.Add(command, "tenant_id", tenantId);

        // 🚨 Istege bagli suzgecler ACIKCA tiplenir: tipsiz bir NULL
        // gonderildiginde PostgreSQL tipi cikaramaz ve 42P08 verir.
        Dialect.AddText(command, "agent_name", query.AgentName);
        Dialect.AddText(command, "session_id", query.SessionId);
        DbHelpers.Add(command, "skip", Math.Max(0, query.Skip));
        DbHelpers.Add(command, "take", Math.Clamp(query.Take, 1, 200));

        return await DbHelpers.ReadListAsync(command, Read, cancellationToken).ConfigureAwait(false);
    }

    private static VoiceSessionRecord Read(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            SessionId = reader.GetString(2),
            AgentName = reader.GetString(3),
            StartedAt = DbHelpers.GetTimestamp(reader, 4),
            EndedAt = DbHelpers.GetNullableTimestamp(reader, 5),
            Turns = reader.GetInt32(6),
            InputSeconds = DbHelpers.GetNullableDecimal(reader, 7),
            OutputChars = DbHelpers.GetNullableInt64(reader, 8),
            EndReason = reader.IsDBNull(9) ? null : (VoiceSessionEndReason)reader.GetInt16(9),
            CreatedBy = DbHelpers.GetNullableString(reader, 10),
        };
}
