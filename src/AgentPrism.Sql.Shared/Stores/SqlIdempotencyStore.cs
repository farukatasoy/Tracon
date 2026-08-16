using System.Data.Common;
using System.Text.Json;

namespace AgentPrism;

/// <summary>Stores idempotency records in the SQL database (Phase 43).</summary>
/// <remarks>
/// <para>
/// The behavior contract is identical to <see cref="InMemoryIdempotencyStore"/>
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
    /// <exception cref="ArgumentNullException"><paramref name="context"/> <see langword="null"/> ise.</exception>
    public SqlIdempotencyStore(SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask<IdempotencyReservation> ReserveAsync(
        IdempotencyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = CreateCommand(_sql.InsertIdempotencyKey);
        DbHelpers.Add(command, "tenant_id", request.TenantId);
        DbHelpers.Add(command, "key", request.Key);
        Dialect.AddText(command, "fingerprint", request.Fingerprint);
        Dialect.AddTimestamp(command, "created_at", request.CreatedAt);

        try
        {
            await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);

            return new IdempotencyReservation { State = IdempotencyState.Reserved };
        }
        catch (DbException ex) when (Dialect.IsUniqueViolation(ex))
        {
            var existing = await ReadAsync(request.TenantId, request.Key, cancellationToken).ConfigureAwait(false)
                ?? throw new AgentPrismException(
                    $"Reservation for idempotency key '{request.Key}' conflicted but the record could not be read.", ex);

            if (existing.State != IdempotencyState.Completed)
            {
                return new IdempotencyReservation { State = IdempotencyState.InProgress };
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
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(response);

        var command = CreateCommand(_sql.CompleteIdempotencyKey);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "key", key);
        Dialect.AddInt32(command, "status_code", response.StatusCode);
        Dialect.AddText(command, "content_type", response.ContentType);
        Dialect.AddText(command, "body", response.Body);
        Dialect.AddUuid(command, "run_id", response.RunId);
        Dialect.AddJsonb(command, "headers", SerializeHeaders(response.Headers));
        Dialect.AddTimestamp(command, "completed_at", DateTimeOffset.UtcNow);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask ReleaseAsync(string tenantId, string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var command = CreateCommand(_sql.DeleteIdempotencyKey);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "key", key);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<StoredEntry?> ReadAsync(string tenantId, string key, CancellationToken cancellationToken)
    {
        var command = CreateCommand(_sql.SelectIdempotencyKey);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "key", key);

        return await DbHelpers.ReadSingleAsync(command, Read, cancellationToken).ConfigureAwait(false);
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    private static StoredEntry Read(DbDataReader reader)
    {
        var state = (IdempotencyState)reader.GetInt16(0);
        var fingerprint = reader.GetString(1);

        if (state != IdempotencyState.Completed)
        {
            return new StoredEntry(state, fingerprint, null);
        }

        var response = new IdempotencyResponse
        {
            StatusCode = reader.GetInt32(2),
            ContentType = DbHelpers.GetNullableString(reader, 3) ?? string.Empty,
            Body = DbHelpers.GetNullableString(reader, 4) ?? string.Empty,
            RunId = DbHelpers.GetNullableGuid(reader, 5),
            Headers = DeserializeHeaders(DbHelpers.GetNullableString(reader, 6)),
        };

        return new StoredEntry(state, fingerprint, response);
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
            AgentPrismJsonContext.Default.DictionaryStringString);
    }

    private static Dictionary<string, string> DeserializeHeaders(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var parsed = JsonSerializer.Deserialize(json, AgentPrismJsonContext.Default.DictionaryStringString);

        return parsed is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
    }

    private sealed record StoredEntry(IdempotencyState State, string Fingerprint, IdempotencyResponse? Response);
}
