using BenchmarkDotNet.Attributes;
using Microsoft.Agents.AI;

namespace Tracon.Benchmarks;

/// <summary>
/// Measures the allocation of a cache HIT - the steady state once an agent
/// definition is compiled once; every subsequent run of the same
/// (tenant, name, definition fingerprint, dependency fingerprint, culture) reuses the
/// compiled agent - and of the eviction a full cache runs when a miss needs room.
/// </summary>
[MemoryDiagnoser]
public class CompiledAgentCacheBenchmarks
{
    private const string TenantId = "bench-tenant";
    private const string Name = "bench-agent";
    private const string DefinitionFingerprint = "A1B2C3";
    private const string DependencyFingerprint = "";
    private const string Culture = "";

    // More tenants than the cache holds, taken in turn: a tenant comes round again only after the cache has dropped
    // it, so every store is a miss.
    private const int TenantPool = 16_384;

    private CompiledAgentCache _cache = null!;
    private CompiledAgentCache _fullCache = null!;
    private string[] _tenants = null!;
    private int _storesPerEviction;
    private int _nextTenant;

    // Built ONCE and reused: a fresh lambda expression at the call site inside
    // the benchmark method would itself allocate a closure every call, which
    // would measure THIS class's setup cost, not CompiledAgentCache.GetOrAdd's.
    private Func<AIAgent> _factory = null!;

    [GlobalSetup]
    public void Setup()
    {
        _cache = new CompiledAgentCache();
        _factory = static () => new PlaceholderAgent();

        // Warm the single entry the benchmark then hits repeatedly.
        _cache.GetOrAdd(TenantId, Name, DefinitionFingerprint, DependencyFingerprint, Culture, _factory);

        var options = new TraconAgentCacheOptions();
        _fullCache = new CompiledAgentCache(options, TimeProvider.System);
        _tenants = Enumerable.Range(0, TenantPool).Select(static index => $"tenant-{index}").ToArray();

        // A full cache drops a tenth of its entries when a miss needs room, then takes stores until it is full again:
        // that many stores make one eviction, and an invocation is exactly that many.
        _storesPerEviction = Math.Max(1, options.MaxEntries / 10);

        for (var store = 0; store < options.MaxEntries; store++)
        {
            Store();
        }
    }

    [Benchmark]
    public AIAgent CacheHit() => _cache.GetOrAdd(TenantId, Name, DefinitionFingerprint, DependencyFingerprint, Culture, _factory);

    /// <summary>One eviction of a full cache and the stores that fill it again: the cost of the misses that follow a drop.</summary>
    [Benchmark]
    public AIAgent EvictOnMiss()
    {
        AIAgent last = null!;

        for (var store = 0; store < _storesPerEviction; store++)
        {
            last = Store();
        }

        return last;
    }

    private AIAgent Store()
        => _fullCache.GetOrAdd(_tenants[_nextTenant++ & (TenantPool - 1)], Name, DefinitionFingerprint, DependencyFingerprint, Culture, _factory);
}
