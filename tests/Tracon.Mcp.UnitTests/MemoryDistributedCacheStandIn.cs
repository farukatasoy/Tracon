namespace Microsoft.Extensions.Caching.Distributed;

/// <summary>
/// Stands in for the cache <c>AddDistributedMemoryCache</c> registers. The startup check finds that
/// cache by its full name, so a type with the same name proves the warning without a package that
/// the unit test project does not use.
/// </summary>
internal sealed class MemoryDistributedCache : IDistributedCache
{
    public byte[]? Get(string key) => null;

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);

    public void Refresh(string key)
    {
    }

    public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

    public void Remove(string key)
    {
    }

    public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
    }

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        => Task.CompletedTask;
}
