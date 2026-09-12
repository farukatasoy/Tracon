using System.Collections.Concurrent;

namespace Tracon;

/// <summary>The default implementation that keeps API keys in process memory.</summary>
/// <remarks>
/// For single-process deployments and tests. <c>UsePostgreSql()</c>, or the
/// SQL Server or SQLite equivalent, replaces it with <c>SqlApiKeyStore</c>.
/// </remarks>
internal sealed class InMemoryApiKeyStore : IApiKeyStore
{
    private readonly ConcurrentDictionary<Guid, StoredApiKey> _keys = new();
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new in-memory API key store.</summary>
    /// <param name="timeProvider">The time provider. Uses <see cref="TimeProvider.System"/> when omitted.</param>
    public InMemoryApiKeyStore(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public ValueTask<ApiKeyCreationResult> CreateAsync(ApiKeyDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var generated = ApiKeyGenerator.Generate(draft.TenantId);
        var record = new ApiKeyRecord
        {
            Id = TraconId.NewId(),
            TenantId = draft.TenantId,
            Name = draft.Name,
            KeyPrefix = generated.KeyPrefix,
            Scopes = draft.Scopes,
            ExpiresAt = draft.ExpiresAt,
            CreatedAt = _timeProvider.GetUtcNow(),
        };

        _keys[record.Id] = new StoredApiKey { Record = record, KeyHash = generated.KeyHash };

        return new ValueTask<ApiKeyCreationResult>(new ApiKeyCreationResult
        {
            Record = record,
            PlaintextKey = generated.PlaintextKey,
        });
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ApiKeyRecord>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        IReadOnlyList<ApiKeyRecord> result = _keys.Values
            .Where(stored => string.Equals(stored.Record.TenantId, tenantId, StringComparison.Ordinal))
            .Select(static stored => stored.Record)
            .OrderBy(static record => record.CreatedAt)
            .ToList();

        return new ValueTask<IReadOnlyList<ApiKeyRecord>>(result);
    }

    /// <inheritdoc />
    public ValueTask<ApiKeyRecord?> FindByHashAsync(ReadOnlyMemory<byte> keyHash, CancellationToken cancellationToken = default)
    {
        foreach (var stored in _keys.Values)
        {
            if (stored.KeyHash.AsSpan().SequenceEqual(keyHash.Span))
            {
                return new ValueTask<ApiKeyRecord?>(stored.Record);
            }
        }

        return new ValueTask<ApiKeyRecord?>((ApiKeyRecord?)null);
    }

    /// <inheritdoc />
    public ValueTask<bool> RevokeAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        if (!_keys.TryGetValue(id, out var stored)
            || !string.Equals(stored.Record.TenantId, tenantId, StringComparison.Ordinal)
            || stored.Record.RevokedAt is not null)
        {
            return new ValueTask<bool>(false);
        }

        _keys[id] = stored with { Record = stored.Record with { RevokedAt = _timeProvider.GetUtcNow() } };

        return new ValueTask<bool>(true);
    }

    /// <inheritdoc />
    public ValueTask TouchLastUsedAsync(Guid id, DateTimeOffset usedAt, CancellationToken cancellationToken = default)
    {
        if (_keys.TryGetValue(id, out var stored))
        {
            _keys[id] = stored with { Record = stored.Record with { LastUsedAt = usedAt } };
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask<bool> HasActiveScopeAsync(ApiKeyScope scope, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        var found = _keys.Values.Any(stored =>
            stored.Record.RevokedAt is null
            && (stored.Record.ExpiresAt is null || stored.Record.ExpiresAt > now)
            && stored.Record.Scopes.Contains(scope));

        return new ValueTask<bool>(found);
    }

    private sealed record StoredApiKey
    {
        public required ApiKeyRecord Record { get; init; }

        public required byte[] KeyHash { get; init; }
    }
}
