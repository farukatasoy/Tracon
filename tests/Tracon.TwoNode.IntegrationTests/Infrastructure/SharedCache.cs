using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;

namespace Tracon.TwoNode.IntegrationTests.Infrastructure;

/// <summary>The cache the two hosts share: one object, as one Redis is for two replicas.</summary>
internal sealed class SharedCache : IDistributedCache
{
    private readonly ConcurrentDictionary<string, byte[]> _entries = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Keys => [.. _entries.Keys];

    public byte[]? Peek(string key) => _entries.TryGetValue(key, out var value) ? value : null;

    public byte[]? Get(string key) => Peek(key)?.ToArray();

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));

    public void Refresh(string key)
    {
    }

    public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

    public void Remove(string key) => _entries.TryRemove(key, out _);

    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        Remove(key);

        return Task.CompletedTask;
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _entries[key] = value.ToArray();

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        Set(key, value, options);

        return Task.CompletedTask;
    }
}
