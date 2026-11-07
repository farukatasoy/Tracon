using System.Data.Common;
using System.Text.Json;

namespace Tracon;

/// <summary>Stores idempotency records in the SQL database.</summary>
/// <remarks>
/// <para>
/// The behavior contract is identical to <c>InMemoryIdempotencyStore</c>
/// and is guarded by the shared contract tests.
/// </para>
/// <para>
/// A reservation is a PLAIN <c>INSERT</c>; a second request with the same key
/// falls into a uniqueness violation and is caught with
/// <see cref="SqlDialect.IsUniqueViolation"/> — the same way
/// <c>SqlExperimentStore.StartAsync</c> does it. Only two states persist in the
/// database: <c>Reserved</c> (0) and <c>Completed</c> (2);
/// <c>InProgress</c>/<c>FingerprintMismatch</c> are derived at read time.
/// </para>
/// </remarks>
internal sealed class SqlIdempotencyStore : IIdempotencyStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Creates a new SQL idempotency store.</summary>
    /// <param name="context">The store context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public SqlIdempotencyStore(SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    private const int MaxReserveAttempts = 5;

    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    /// <remarks>
    /// The row that made the insert conflict can be gone when it is read: the request that held it released the key
    /// (a failed request does) between the two statements. The reservation then starts again, so a retry that arrives
    /// while the first attempt is failing reserves the key instead of failing with the conflict it lost.
    /// </remarks>
    public async ValueTask<IdempotencyReservation> ReserveAsync(
        IdempotencyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        for (var attempt = 0; attempt < MaxReserveAttempts; attempt++)
        {
            if (await TryReserveOnceAsync(request, cancellationToken).ConfigureAwait(false) is { } reservation)
            {
                return reservation;
            }
        }

        throw new TraconException(
            $"Reservation for idempotency key '{request.Key}' conflicted with a record that was gone each time it was read.");
    }

    private async ValueTask<IdempotencyReservation?> TryReserveOnceAsync(
        IdempotencyRequest request,
        CancellationToken cancellationToken)
    {

        try
        {
            await _context.ExecuteStatementAsync(
            _sql.InsertIdempotencyKey,
            command =>
            {
                DbHelpers.AddTenant(command, request.TenantId);
                DbHelpers.Add(command, "key", request.Key);
                Dialect.AddText(command, "fingerprint", request.Fingerprint);
                Dialect.AddTimestamp(command, "created_at", request.CreatedAt);
            },
            DbHelpers.ExecuteAsync,
            cancellationToken).ConfigureAwait(false);

            return new IdempotencyReservation { State = IdempotencyState.Reserved };
        }
        catch (DbException ex) when (Dialect.IsUniqueViolation(ex))
        {
            // A reservation nobody finished (the process died between the reservation and the response) is taken over by the
            // first request that finds it old enough. The row does the choosing: of several requests that race for it, one
            // changes it and the others find it newer than their cutoff.
            var cutoff = default(DateTimeOffset);
            var canTakeOver = request.ReservationTimeout is { } timeout && TryGetCutoff(request.CreatedAt, timeout, out cutoff);

            if (canTakeOver && await TakeOverAsync(request, cutoff, cancellationToken).ConfigureAwait(false))
            {
                return new IdempotencyReservation { State = IdempotencyState.Reserved };
            }

            var existing = await ReadAsync(request.TenantId, request.Key, cancellationToken).ConfigureAwait(false);

            if (existing is null)
            {
                // Released between the conflict and the read: the key is free, so the caller reserves it afresh.
                return null;
            }

            if (existing.State != IdempotencyState.Completed)
            {
                // 🚨 InProgress says a reservation that is still young stands in the way. The row read here can be another row than
                // the one the takeover missed: the key was released and reserved again between the two statements. When it is
                // old enough for the takeover, starting over takes it over (or finds a young one); answering InProgress would
                // describe a moment in which no young reservation existed, which no serial order of the requests allows.
                return canTakeOver && existing.CreatedAt <= cutoff
                    ? null
                    : new IdempotencyReservation { State = IdempotencyState.InProgress };
            }

            return string.Equals(existing.Fingerprint, request.Fingerprint, StringComparison.Ordinal)
                ? new IdempotencyReservation { State = IdempotencyState.Completed, Response = existing.Response }
                : new IdempotencyReservation { State = IdempotencyState.FingerprintMismatch };
        }
    }

    /// <inheritdoc />
    public async ValueTask CompleteAsync(
        string tenantId,
        string key,
        IdempotencyResponse response,
        DateTimeOffset? reservedAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(response);

        await _context.ExecuteStatementAsync(
            _sql.CompleteIdempotencyKey,
            command =>
            {
                DbHelpers.AddTenant(command, tenantId);
                DbHelpers.Add(command, "key", key);
                Dialect.AddInt32(command, "status_code", response.StatusCode);
                Dialect.AddText(command, "content_type", response.ContentType);
                Dialect.AddText(command, "body", response.Body);
                Dialect.AddUuid(command, "run_id", response.RunId);
                Dialect.AddJsonb(command, "headers", SerializeHeaders(response.Headers));
                Dialect.AddTimestamp(command, "completed_at", DateTimeOffset.UtcNow);
                Dialect.AddTimestamp(command, "reserved_at", reservedAt);
            },
            DbHelpers.ExecuteAsync,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask ReleaseAsync(
        string tenantId,
        string key,
        DateTimeOffset? reservedAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await _context.ExecuteStatementAsync(
            _sql.DeleteIdempotencyKey,
            command =>
            {
                DbHelpers.AddTenant(command, tenantId);
                DbHelpers.Add(command, "key", key);
                Dialect.AddTimestamp(command, "reserved_at", reservedAt);
            },
            DbHelpers.ExecuteAsync,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The time a reservation has to be at or before to be taken over. A timeout that is zero, negative, or longer than the
    /// time since the Unix epoch takes nothing over: no reservation is older than that, and subtracting it would run off the
    /// range of a <see cref="DateTimeOffset"/> (a consumer can pass <see cref="TimeSpan.MaxValue"/> to mean "never").
    /// </summary>
    private static bool TryGetCutoff(DateTimeOffset now, TimeSpan timeout, out DateTimeOffset cutoff)
    {
        cutoff = default;

        if (timeout <= TimeSpan.Zero || timeout >= now - DateTimeOffset.UnixEpoch)
        {
            return false;
        }

        cutoff = now - timeout;

        return true;
    }

    private async ValueTask<bool> TakeOverAsync(IdempotencyRequest request, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        return await _context.ExecuteStatementAsync(
            _sql.TakeOverIdempotencyKey,
            command =>
            {
                DbHelpers.AddTenant(command, request.TenantId);
                DbHelpers.Add(command, "key", request.Key);
                Dialect.AddText(command, "fingerprint", request.Fingerprint);
                Dialect.AddTimestamp(command, "created_at", request.CreatedAt);
                Dialect.AddTimestamp(command, "cutoff", cutoff);
            },
            DbHelpers.ExecuteAsync,
            cancellationToken).ConfigureAwait(false) == 1;
    }

    private async ValueTask<StoredEntry?> ReadAsync(string tenantId, string key, CancellationToken cancellationToken)
    {
        return await _context.ExecuteStatementAsync(
            _sql.SelectIdempotencyKey,
            command =>
            {
                DbHelpers.AddTenant(command, tenantId);
                DbHelpers.Add(command, "key", key);
            },
            (command, token) => DbHelpers.ReadSingleAsync(command, Read, token),
            cancellationToken).ConfigureAwait(false);
    }

    private static StoredEntry Read(DbDataReader reader)
    {
        var state = (IdempotencyState)reader.GetInt16(0);
        var fingerprint = reader.GetString(1);
        var createdAt = DbHelpers.GetTimestamp(reader, 7);

        if (state != IdempotencyState.Completed)
        {
            return new StoredEntry(state, fingerprint, null, createdAt);
        }

        var response = new IdempotencyResponse
        {
            StatusCode = reader.GetInt32(2),
            ContentType = DbHelpers.GetNullableString(reader, 3) ?? string.Empty,
            Body = DbHelpers.GetNullableString(reader, 4) ?? string.Empty,
            RunId = DbHelpers.GetNullableGuid(reader, 5),
            Headers = DeserializeHeaders(DbHelpers.GetNullableString(reader, 6)),
        };

        return new StoredEntry(state, fingerprint, response, createdAt);
    }

    // 🚨 Source-generated context: reflection-based serialization is not used,
    // for AOT compatibility (same pattern as
    // SqlWebhookStore.SerializeHeaders/DeserializeHeaders).
    private static string? SerializeHeaders(IReadOnlyDictionary<string, string> headers)
    {
        if (headers.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(
            headers.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            TraconJsonContext.Default.DictionaryStringString);
    }

    private static Dictionary<string, string> DeserializeHeaders(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var parsed = JsonSerializer.Deserialize(json, TraconJsonContext.Default.DictionaryStringString);

        return parsed is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
    }

    private sealed record StoredEntry(IdempotencyState State, string Fingerprint, IdempotencyResponse? Response, DateTimeOffset CreatedAt);
}
