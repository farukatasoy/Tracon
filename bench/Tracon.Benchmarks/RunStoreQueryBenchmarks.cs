using System.Globalization;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon.Benchmarks;

/// <summary>
/// Measures the allocation of a selected <c>store</c> query -
/// <see cref="SqlRunStore.QueryRunsAsync"/> is what list endpoints and the
/// reconciliation scan return (docs/arsiv/fazlar/116-PERFORMANS-TAHSIS-KAPISI.md, 116.2).
/// </summary>
/// <remarks>
/// Measured on SQLite, not PostgreSQL/SQL Server: no Docker dependency, and
/// allocation on the managed side is dominated by command setup and row
/// reading, not by the wire dialect (116.2, third row).
/// </remarks>
[MemoryDiagnoser]
public class RunStoreQueryBenchmarks : IDisposable
{
    private const int SeedRunCount = 200;
    private const string AgentName = "bench-agent";

    private string _databasePath = null!;
    private SqliteDataSource _dataSource = null!;
    private SqlStoreContext _context = null!;
    private SqlRunStore _store = null!;
    private RunQuery _query = null!;

    [GlobalSetup]
    public void Setup()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"tracon-bench-{Guid.NewGuid():N}.db");
        _dataSource = new SqliteDataSource($"Data Source={_databasePath}");

        var tablePrefix = "bench_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12] + "_";

        _context = new SqlStoreContext
        {
            DataSource = _dataSource,
            Dialect = new SqliteDialect(tablePrefix),
            CommandTimeoutSeconds = 30,
            ProviderName = "SQLite",
        };

        var migrations = new MigrationRunner(_context, NullLogger<MigrationRunner>.Instance);
        migrations.ApplyAsync().AsTask().GetAwaiter().GetResult();

        var tenantContext = new FixedTenantContext("bench-tenant");
        _store = new SqlRunStore(_context, tenantContext);

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

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    /// <summary>Satisfies CA1001 (the class owns <see cref="SqlStoreContext"/>, which owns the data source); BenchmarkDotNet itself calls <see cref="Cleanup"/>, not this.</summary>
    public void Dispose()
    {
        _context?.Dispose();
        if (_databasePath is not null && File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        GC.SuppressFinalize(this);
    }

    [Benchmark]
    public async Task<int> QueryRuns() => (await _store.QueryRunsAsync(_query).ConfigureAwait(false)).Count;
}
