using System.Data.Common;

namespace AgentPrism;

/// <summary>Idempotency kayitlarini SQL'de saklayan depo (Faz 43).</summary>
/// <remarks>
/// <para>
/// Davranis sozlesmesi <see cref="InMemoryIdempotencyStore"/> ile birebir aynidir
/// ve ortak sozlesme testleriyle korunur.
/// </para>
/// <para>
/// Ayirma DUZ bir <c>INSERT</c>'tir; ikinci bir ayni-anahtarli istek benzersizlik
/// ihlaline duser ve <see cref="SqlDialect.IsUniqueViolation"/> ile yakalanir —
/// tipki <c>SqlExperimentStore.StartAsync</c>'in yaptigi gibi. Veritabaninda
/// yalniz iki durum kalicidir: <c>Reserved</c> (0) ve <c>Completed</c> (2);
/// <c>InProgress</c>/<c>FingerprintMismatch</c> okuma aninda turetilir.
/// </para>
/// </remarks>
internal sealed class SqlIdempotencyStore : IIdempotencyStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Yeni bir SQL idempotency deposu olusturur.</summary>
    /// <param name="context">Depo baglami.</param>
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
                    $"'{request.Key}' idempotency anahtari icin ayirma catisti ama kayit okunamadi.", ex);

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
        };

        return new StoredEntry(state, fingerprint, response);
    }

    private sealed record StoredEntry(IdempotencyState State, string Fingerprint, IdempotencyResponse? Response);
}
