using System.Data.Common;

namespace AgentPrism;

/// <summary>Kiraci bazli API anahtarlarini saklayan depo.</summary>
/// <remarks>
/// 🚨 Bu depo hicbir zaman ham anahtar degeri yazmaz veya okumaz; yalnizca
/// geri donduruleyemez bir SHA-256 ozeti tutar
/// (docs/53-KIRACI-API-ANAHTARLARI.md, bolum 53.2).
/// </remarks>
internal sealed class SqlApiKeyStore : IApiKeyStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;
    private readonly TimeProvider _timeProvider;

    /// <summary>Yeni bir API anahtari deposu olusturur.</summary>
    /// <param name="context">Depo baglami.</param>
    /// <param name="timeProvider">Zaman kaynagi. Verilmezse <see cref="TimeProvider.System"/> kullanilir.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> <see langword="null"/> ise.</exception>
    public SqlApiKeyStore(SqlStoreContext context, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Saglayiciya ozgu davranislarin kapisi.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask<ApiKeyCreationResult> CreateAsync(ApiKeyDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var generated = ApiKeyGenerator.Generate(draft.TenantId);
        var record = new ApiKeyRecord
        {
            Id = AgentPrismId.NewId(),
            TenantId = draft.TenantId,
            Name = draft.Name,
            KeyPrefix = generated.KeyPrefix,
            Scopes = draft.Scopes,
            ExpiresAt = draft.ExpiresAt,
            CreatedAt = _timeProvider.GetUtcNow(),
        };

        var command = CreateCommand(_sql.InsertApiKey);
        DbHelpers.Add(command, "id", record.Id);
        DbHelpers.Add(command, "tenant_id", record.TenantId);
        DbHelpers.Add(command, "name", record.Name);
        Dialect.AddBinary(command, "key_hash", generated.KeyHash);
        DbHelpers.Add(command, "key_prefix", record.KeyPrefix);
        Dialect.AddTextArray(command, "scopes", ScopeNames(record.Scopes));
        Dialect.AddTimestamp(command, "expires_at", record.ExpiresAt);
        Dialect.AddTimestamp(command, "revoked_at", record.RevokedAt);
        Dialect.AddTimestamp(command, "last_used_at", record.LastUsedAt);
        Dialect.AddTimestamp(command, "created_at", record.CreatedAt);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);

        return new ApiKeyCreationResult { Record = record, PlaintextKey = generated.PlaintextKey };
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ApiKeyRecord>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectApiKeys);
        DbHelpers.Add(command, "tenant_id", tenantId);

        return await DbHelpers.ReadListAsync(command, ReadRecord, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Kiraci bu cagrinin girdisi degil ciktisidir: bir istegi dogrularken hangi kiraciya ait oldugunu henuz bilmeyiz (bolum 53.5).")]
    public async ValueTask<ApiKeyRecord?> FindByHashAsync(ReadOnlyMemory<byte> keyHash, CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectApiKeyByHash);
        Dialect.AddBinary(command, "key_hash", keyHash.ToArray());

        return await DbHelpers.ReadSingleAsync(command, ReadRecord, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> RevokeAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.RevokeApiKey);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "id", id);
        Dialect.AddTimestamp(command, "revoked_at", _timeProvider.GetUtcNow());

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Cagiran (ApiKeyAuthenticator) anahtari zaten ozet uzerinden bulmus ve kiraciyi cozmustur; FindByHashAsync ile ayni gerekce.")]
    public async ValueTask TouchLastUsedAsync(Guid id, DateTimeOffset usedAt, CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.TouchApiKeyLastUsed);
        DbHelpers.Add(command, "id", id);
        Dialect.AddTimestamp(command, "last_used_at", usedAt);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Kurulum saglik denetimidir (ExternalSurfaceGuard, bolum 53.4); belirli bir kiraciya ozgu degildir.")]
    public async ValueTask<bool> HasActiveScopeAsync(ApiKeyScope scope, CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.HasApiKeyWithScope);
        DbHelpers.Add(command, "scope", scope.ToString());
        Dialect.AddTimestamp(command, "now", _timeProvider.GetUtcNow());

        var result = await DbHelpers.ExecuteScalarAsync(command, cancellationToken).ConfigureAwait(false);

        return result is not null && DbHelpers.ToBoolean(result);
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    private static IReadOnlyList<string> ScopeNames(IReadOnlyList<ApiKeyScope> scopes)
        => [.. scopes.Select(static scope => scope.ToString())];

    private ApiKeyRecord ReadRecord(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            Name = reader.GetString(2),

            // Sutun 3 (key_hash) BILEREK atlanir: ozet ApiKeyRecord'a hic
            // girmez (bolum 53.2).
            KeyPrefix = reader.GetString(4),
            Scopes = [.. Dialect.ReadTextArray(reader, 5).Select(ParseScope)],
            ExpiresAt = DbHelpers.GetNullableTimestamp(reader, 6),
            RevokedAt = DbHelpers.GetNullableTimestamp(reader, 7),
            LastUsedAt = DbHelpers.GetNullableTimestamp(reader, 8),
            CreatedAt = DbHelpers.GetTimestamp(reader, 9),
        };

    private static ApiKeyScope ParseScope(string name) => Enum.Parse<ApiKeyScope>(name);
}
