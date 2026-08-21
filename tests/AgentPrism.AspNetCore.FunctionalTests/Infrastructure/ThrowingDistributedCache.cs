using Microsoft.Extensions.Caching.Distributed;

namespace AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>An <see cref="IDistributedCache"/> that throws on every call, registered through DI like a real store.</summary>
/// <remarks>
/// Proves the "observability never breaks functionality" rule through the SAME
/// boundary the real defect (usage double-reported on a hit) was found at:
/// HTTP, not the client constructed directly.
/// </remarks>
internal sealed class ThrowingDistributedCache : IDistributedCache
{
    public byte[]? Get(string key) => throw new InvalidOperationException("The store is unavailable.");

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        => Task.FromException<byte[]?>(new InvalidOperationException("The store is unavailable."));

    public void Refresh(string key) => throw new InvalidOperationException("The store is unavailable.");

    public Task RefreshAsync(string key, CancellationToken token = default)
        => Task.FromException(new InvalidOperationException("The store is unavailable."));

    public void Remove(string key) => throw new InvalidOperationException("The store is unavailable.");

    public Task RemoveAsync(string key, CancellationToken token = default)
        => Task.FromException(new InvalidOperationException("The store is unavailable."));

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        => throw new InvalidOperationException("The store is unavailable.");

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        => Task.FromException(new InvalidOperationException("The store is unavailable."));
}
