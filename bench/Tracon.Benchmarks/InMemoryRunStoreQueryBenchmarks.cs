using BenchmarkDotNet.Attributes;

namespace Tracon.Benchmarks;

/// <summary>
/// Measures the allocation of a selected <c>store</c> query on the in-memory store - the development default, and the store
/// every test host and every sample starts with.
/// </summary>
/// <remarks>
/// Each row of a page gets its child count and its tree total computed at read time, from the runs the store holds. A
/// computation that copied the whole run table once for every row of the page paid <c>page size x run count</c> references
/// for a single list call - about 444 KB for a page of 50 over the store's own limit of 1,000 runs - although the loop that
/// follows the copy does the same work as a loop over the table itself. The seed is that limit, and the page is the size the
/// list endpoints default to.
/// </remarks>
[MemoryDiagnoser]
public class InMemoryRunStoreQueryBenchmarks
{
    private const int SeedRunCount = 1_000;
    private const string AgentName = "bench-agent";

    private InMemoryRunStore _store = null!;
    private RunQuery _query = null!;

    [GlobalSetup]
    public void Setup()
    {
        var tenantContext = new FixedTenantContext("bench-tenant");
        _store = new InMemoryRunStore(tenantContext: tenantContext) { MaxRuns = SeedRunCount };

        var startedAt = DateTimeOffset.UtcNow.AddDays(-1);

        for (var i = 0; i < SeedRunCount; i++)
        {
            _store.StartRunAsync(new RunStartInfo
            {
                RunId = Guid.NewGuid(),
                AgentName = AgentName,
                StartedAt = startedAt.AddSeconds(i),
                Status = RunStatus.Completed,
                TenantId = tenantContext.TenantId,
            }).AsTask().GetAwaiter().GetResult();
        }

        _query = new RunQuery { AgentName = AgentName, Take = 50 };
    }

    [Benchmark]
    public async Task<int> QueryRuns() => (await _store.QueryRunsAsync(_query).ConfigureAwait(false)).Count;
}
