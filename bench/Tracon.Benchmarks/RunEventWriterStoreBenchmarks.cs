using System.Globalization;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon.Benchmarks;

/// <summary>
/// Measures what 200 streamed text chunks cost the store, written one by one and written as one group, on a real SQLite
/// file - <see cref="RunEventWriter"/> hands the store a group when the stream outruns it, one chunk when it does not.
/// </summary>
/// <remarks>
/// <para>
/// A real database and not the no-op store of <see cref="RunEventWriterBenchmarks"/>: that one isolates the writer, whose
/// own part is a hundred bytes a chunk, and says nothing of the connection, the command and the parameters each store call
/// makes. Every call here is a statement the way a run makes it.
/// </para>
/// <para>
/// The group is written by the store directly and not through the writer's drain on purpose. The size of the groups a
/// drain forms follows the timing of the machine, and the allocation of a group follows its size; a number that moved with
/// the load of the runner would fail a gate that compares bytes. The part the drain adds is a few instructions and no
/// allocation per group, which <see cref="RunEventWriterBenchmarks"/> measures.
/// </para>
/// <para>
/// Each operation opens a run of its own and builds its 200 events; <see cref="BuildOnly"/> is that part alone, so a reader
/// who wants the cost of the writes subtracts it from the other two.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class RunEventWriterStoreBenchmarks : IDisposable
{
    private const int Chunks = 200;

    private string _databasePath = null!;
    private SqliteDataSource _dataSource = null!;
    private SqlStoreContext _context = null!;
    private SqlRunStore _store = null!;
    private FixedTenantContext _tenant = null!;

    [GlobalSetup]
    public void Setup()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"tracon-bench-events-{Guid.NewGuid():N}.db");
        _dataSource = new SqliteDataSource($"Data Source={_databasePath}");

        var tablePrefix = "bench_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12] + "_";

        _context = new SqlStoreContext
        {
            DataSource = _dataSource,
            Dialect = new SqliteDialect(tablePrefix),
            CommandTimeoutSeconds = 30,
            ProviderName = "SQLite",
        };

        new MigrationRunner(_context, NullLogger<MigrationRunner>.Instance).ApplyAsync().AsTask().GetAwaiter().GetResult();

        _tenant = new FixedTenantContext("bench-tenant");
        _store = new SqlRunStore(_context, _tenant);
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    /// <summary>Satisfies CA1001 (the class owns <see cref="SqlStoreContext"/>, which owns the data source); BenchmarkDotNet itself calls <see cref="Cleanup"/>, not this.</summary>
    public void Dispose()
    {
        _context?.Dispose();

        if (_databasePath is not null)
        {
            foreach (var file in Directory.GetFiles(Path.GetDirectoryName(_databasePath)!, Path.GetFileName(_databasePath) + "*"))
            {
                File.Delete(file);
            }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>The run and the 200 events, and no write of an event: what the other two share.</summary>
    [Benchmark]
    public async Task<int> BuildOnly()
    {
        var (_, events) = await OpenRunAsync().ConfigureAwait(false);

        return events.Length;
    }

    /// <summary>The chunks one after the other, each a statement of its own: what a stream paid for each chunk.</summary>
    [Benchmark]
    public async Task<int> EachChunkOnItsOwn()
    {
        var (_, events) = await OpenRunAsync().ConfigureAwait(false);

        foreach (var runEvent in events)
        {
            await _store.AppendEventAsync(runEvent).ConfigureAwait(false);
        }

        return events.Length;
    }

    /// <summary>The same chunks as one group in one statement.</summary>
    [Benchmark]
    public async Task<int> AllChunksAsOneGroup()
    {
        var (_, events) = await OpenRunAsync().ConfigureAwait(false);

        await _store.AppendEventsAsync(events).ConfigureAwait(false);

        return events.Length;
    }

    private async Task<(Guid RunId, RunEvent[] Events)> OpenRunAsync()
    {
        var runId = Guid.NewGuid();
        var at = DateTimeOffset.UtcNow;

        await _store.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "bench-agent",
            StartedAt = at,
            Status = RunStatus.Running,
            TenantId = _tenant.TenantId,
        }).ConfigureAwait(false);

        var events = new RunEvent[Chunks];

        for (var i = 0; i < Chunks; i++)
        {
            events[i] = new RunEvent
            {
                RunId = runId,
                Sequence = i,
                Type = RunEventType.MessageDelta,
                Timestamp = at,
                Text = "a reasonably sized streamed text delta",
                TenantId = _tenant.TenantId,
            };
        }

        return (runId, events);
    }
}
