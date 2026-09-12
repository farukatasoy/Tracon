using Microsoft.Extensions.Caching.Distributed;

namespace Tracon.Core.UnitTests.Fakes;

/// <summary>
/// An in-memory <see cref="IDistributedCache"/> fake; no external cache
/// dependency and no time-based eviction (entries live for the fake's own
/// lifetime).
/// </summary>
internal sealed class FakeDistributedCache : IDistributedCache
{
    private readonly Dictionary<string, byte[]> _entries = new(StringComparer.Ordinal);

    /// <summary>The options passed to the most recent <see cref="Set"/>/<see cref="SetAsync"/> call.</summary>
    public DistributedCacheEntryOptions? LastSetOptions { get; private set; }

    /// <summary>The number of <see cref="Set"/>/<see cref="SetAsync"/> calls.</summary>
    public int SetCount { get; private set; }

    /// <summary>When set, every <see cref="GetAsync"/> call throws this instead of reading.</summary>
    public Exception? ThrowOnGet { get; set; }

    /// <summary>When set, every <see cref="SetAsync"/> call throws this instead of writing.</summary>
    public Exception? ThrowOnSet { get; set; }

    public byte[]? Get(string key) => _entries.TryGetValue(key, out var value) ? value : null;

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        => ThrowOnGet is { } exception ? Task.FromException<byte[]?>(exception) : Task.FromResult(Get(key));

    public void Refresh(string key)
    {
        // No sliding expiration in this fake.
    }

    public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

    public void Remove(string key) => _entries.Remove(key);

    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        Remove(key);
        return Task.CompletedTask;
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        _entries[key] = value;
        LastSetOptions = options;
        SetCount++;
    }

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        if (ThrowOnSet is { } exception)
        {
            return Task.FromException(exception);
        }

        Set(key, value, options);
        return Task.CompletedTask;
    }
}
