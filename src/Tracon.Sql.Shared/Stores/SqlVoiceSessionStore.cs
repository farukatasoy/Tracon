using System.Data.Common;

namespace Tracon;

/// <summary>Persists the summary record of voice conversations.</summary>
/// <remarks>
/// The behavior contract is identical to <c>InMemoryVoiceSessionStore</c>;
/// the only difference is that the in-memory implementation caps the record count.
/// </remarks>
internal sealed class SqlVoiceSessionStore : IVoiceSessionStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Creates a new voice session store.</summary>
    /// <param name="context">The store context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
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
        DbHelpers.AddTenant(command, record.TenantId);
        DbHelpers.Add(command, "session_id", record.SessionId);
        DbHelpers.Add(command, "agent_name", record.AgentName);
        Dialect.AddTimestamp(command, "started_at", record.StartedAt);
        Dialect.AddTimestamp(command, "ended_at", record.EndedAt);
        DbHelpers.Add(command, "turns", record.Turns);
        Dialect.AddDecimal(command, "input_seconds", record.InputSeconds);
        Dialect.AddInt64(command, "output_chars", record.OutputChars);
        Dialect.AddInt16(command, "end_reason", (short?)record.EndReason);
        Dialect.AddText(command, "created_by", record.CreatedBy);
        Dialect.AddText(command, "provider", record.Provider);
        Dialect.AddText(command, "model", record.Model);
        Dialect.AddDecimal(command, "live_seconds", record.LiveSeconds);
        Dialect.AddDecimal(command, "duration_cost", record.Cost?.DurationCost);
        Dialect.AddDecimal(command, "character_cost", record.Cost?.CharacterCost);
        Dialect.AddText(command, "currency", record.Cost?.Currency);

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
        DbHelpers.AddTenant(command, tenantId);

        // 🚨 Optional filters are typed EXPLICITLY: when an untyped NULL is
        // sent, PostgreSQL cannot infer the type and returns 42P08.
        Dialect.AddText(command, "agent_name", query.AgentName);
        Dialect.AddText(command, "session_id", query.SessionId);
        DbHelpers.Add(command, "skip", Math.Max(0, query.Skip));
        DbHelpers.Add(command, "take", Math.Clamp(query.Take, 1, 200));

        return await DbHelpers.ReadListAsync(command, Read, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads one record.</summary>
    /// <remarks>
    /// The ordinals are bare and must track the column list in every dialect's
    /// <c>SelectVoiceSessions</c>. New columns are appended at the END for exactly
    /// this reason: inserting one in the middle shifts every field below it and the
    /// compiler says nothing.
    /// </remarks>
    private static VoiceSessionRecord Read(DbDataReader reader)
    {
        var durationCost = DbHelpers.GetNullableDecimal(reader, 14);
        var characterCost = DbHelpers.GetNullableDecimal(reader, 15);
        var currency = DbHelpers.GetNullableString(reader, 16);

        return new VoiceSessionRecord
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
            Provider = DbHelpers.GetNullableString(reader, 11),
            Model = DbHelpers.GetNullableString(reader, 12),
            LiveSeconds = DbHelpers.GetNullableDecimal(reader, 13),

            // 🚨 A cost of null is not a cost of zero: an unpriced session must not
            // claim it was free, so the record stays null unless an addend was priced.
            Cost = durationCost is null && characterCost is null
                ? null
                : new VoiceSessionCost
                {
                    DurationCost = durationCost,
                    CharacterCost = characterCost,
                    Currency = currency,
                },
        };
    }
}
