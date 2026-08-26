using BenchmarkDotNet.Attributes;
using Microsoft.Agents.AI;

namespace AgentPrism.Benchmarks;

/// <summary>
/// Measures the allocation of a cache HIT - the steady state once an agent
/// definition is compiled once; every subsequent run of the same
/// (tenant, name, version, dependency fingerprint, culture) reuses the
/// compiled agent (docs/116-PERFORMANS-TAHSIS-KAPISI.md, 116.2).
/// </summary>
[MemoryDiagnoser]
public class CompiledAgentCacheBenchmarks
{
    private const string TenantId = "bench-tenant";
    private const string Name = "bench-agent";
    private const int Version = 1;
    private const string DependencyFingerprint = "";
    private const string Culture = "";

    private CompiledAgentCache _cache = null!;

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
        _cache.GetOrAdd(TenantId, Name, Version, DependencyFingerprint, Culture, _factory);
    }

    [Benchmark]
    public AIAgent CacheHit() => _cache.GetOrAdd(TenantId, Name, Version, DependencyFingerprint, Culture, _factory);
}
