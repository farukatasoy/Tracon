using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Caches the health-check results of the registered model providers.
/// </summary>
/// <remarks>
/// <para>
/// The <c>/api/models/health</c> endpoint does not go to the provider on every
/// call; the result is returned from the cache for
/// <see cref="AgentPrismHealthOptions.CacheTtl"/>. <c>refresh: true</c> bypasses
/// the cache and goes to the provider again.
/// </para>
/// <para>
/// This class is NOT tied to <see cref="IModelProviderRegistry"/> — it reads
/// <see cref="IModelProvider"/> singletons directly through
/// <c>IEnumerable&lt;IModelProvider&gt;</c>. This lets it check whether each
/// provider implements <see cref="IModelProviderHealthCheck"/> without adding a
/// member to the <see cref="IModelProvider"/> interface (K4); a provider that
/// does not implement it has a status of <see cref="ModelProviderHealthStatus.Unknown"/>.
/// </para>
/// <para>
/// A provider whose circuit breaker is open is reported as
/// <see cref="ModelProviderHealthStatus.Unhealthy"/> regardless of the raw
/// cached check result — if real chat calls are failing, that is a more
/// reliable signal than a raw connectivity probe. This layer is read FRESH on
/// every call and is not written to the cache.
/// </para>
/// </remarks>
public sealed class ModelProviderHealthCache
{
    private readonly IEnumerable<IModelProvider> _providers;
    private readonly IOptionsMonitor<AgentPrismOptions> _optionsMonitor;
    private readonly ModelProviderCircuitBreaker? _circuitBreaker;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a new health cache.</summary>
    /// <param name="providers">The registered model providers.</param>
    /// <param name="optionsMonitor">Settings used to read the cache TTL.</param>
    /// <param name="circuitBreaker">The circuit breaker used to reflect circuit state into the result.</param>
    /// <param name="timeProvider">The time source. If <see langword="null"/>, the system clock is used.</param>
    /// <exception cref="ArgumentNullException"><paramref name="providers"/> or <paramref name="optionsMonitor"/> is <see langword="null"/>.</exception>
    public ModelProviderHealthCache(
        IEnumerable<IModelProvider> providers,
        IOptionsMonitor<AgentPrismOptions> optionsMonitor,
        ModelProviderCircuitBreaker? circuitBreaker = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(optionsMonitor);

        _providers = providers;
        _optionsMonitor = optionsMonitor;
        _circuitBreaker = circuitBreaker;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Returns the health status of every registered provider (sorted by name).</summary>
    /// <param name="refresh">Whether to bypass the cache and go to the provider again.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>One record per provider.</returns>
    public async ValueTask<IReadOnlyList<ModelProviderHealth>> GetAllAsync(
        bool refresh,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ModelProviderHealth>();

        foreach (var provider in _providers)
        {
            results.Add(await GetOneAsync(provider, refresh, cancellationToken).ConfigureAwait(false));
        }

        results.Sort(static (left, right) => string.CompareOrdinal(left.ProviderName, right.ProviderName));
        return results;
    }

    /// <summary>Returns the health status of a single provider.</summary>
    /// <param name="providerName">The provider name.</param>
    /// <param name="refresh">Whether to bypass the cache and go to the provider again.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The status if the provider is registered; otherwise <see langword="null"/>.</returns>
    public async ValueTask<ModelProviderHealth?> GetAsync(
        string providerName,
        bool refresh,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        var provider = _providers.FirstOrDefault(
            candidate => string.Equals(candidate.Name, providerName, StringComparison.OrdinalIgnoreCase));

        return provider is null
            ? null
            : await GetOneAsync(provider, refresh, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the last known status from the cache without making a network
    /// call. Intended for fast endpoints such as <c>/api/models</c> to fill in a status field.
    /// </summary>
    /// <param name="providerName">The provider name.</param>
    /// <param name="health">The health record, if found.</param>
    /// <returns><see langword="true"/> if a record exists in the cache.</returns>
    public bool TryPeek(string providerName, out ModelProviderHealth health)
    {
        if (_cache.TryGetValue(providerName, out var entry))
        {
            health = ApplyCircuitBreakerOverlay(providerName, entry.Health);
            return true;
        }

        health = null!;
        return false;
    }

    private async ValueTask<ModelProviderHealth> GetOneAsync(
        IModelProvider provider,
        bool refresh,
        CancellationToken cancellationToken)
    {
        var baseHealth = await GetBaseHealthAsync(provider, refresh, cancellationToken).ConfigureAwait(false);
        return ApplyCircuitBreakerOverlay(provider.Name, baseHealth);
    }

    private async ValueTask<ModelProviderHealth> GetBaseHealthAsync(
        IModelProvider provider,
        bool refresh,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        if (!refresh && _cache.TryGetValue(provider.Name, out var cached) && cached.ExpiresAt > now)
        {
            return cached.Health;
        }

        var health = provider is IModelProviderHealthCheck check
            ? await check.CheckHealthAsync(cancellationToken).ConfigureAwait(false)
            : new ModelProviderHealth
            {
                ProviderName = provider.Name,
                Status = ModelProviderHealthStatus.Unknown,
                CheckedAt = now,
            };

        var ttl = _optionsMonitor.CurrentValue.Health.CacheTtl;
        _cache[provider.Name] = new CacheEntry(health, now + ttl);

        return health;
    }

    private ModelProviderHealth ApplyCircuitBreakerOverlay(string providerName, ModelProviderHealth health)
    {
        if (_circuitBreaker is null || !_circuitBreaker.IsOpen(providerName, out var retryAfter))
        {
            return health;
        }

        var circuitDetail = $"Circuit breaker is open. Will retry in {retryAfter?.TotalSeconds ?? 0:F0}s.";

        return health with
        {
            Status = ModelProviderHealthStatus.Unhealthy,
            Detail = string.IsNullOrEmpty(health.Detail) ? circuitDetail : $"{circuitDetail} ({health.Detail})",
        };
    }

    private sealed record CacheEntry(ModelProviderHealth Health, DateTimeOffset ExpiresAt);
}
