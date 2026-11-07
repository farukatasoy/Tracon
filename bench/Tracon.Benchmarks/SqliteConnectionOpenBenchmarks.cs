using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;

namespace Tracon.Benchmarks;

/// <summary>
/// Measures the allocation of opening a pooled SQLite connection from Tracon's data source - every store call makes
/// one, so whatever an open does that it did not have to do is paid by every run event, every query and every claim.
/// </summary>
/// <remarks>
/// The open runs a pragma batch and makes sure the two decimal functions are on the native connection. The functions are
/// registered once per native connection; a registration that came back to every open (the driver's own
/// <c>CreateFunction</c> does that, because it takes its functions off again at close) raised this number from
/// 1,557 B to 3,501 B and showed up in the allocation of every store call. Not measured on PostgreSQL or SQL Server: their
/// connection setup is the driver's, with nothing of Tracon's in it.
/// </remarks>
[MemoryDiagnoser]
public class SqliteConnectionOpenBenchmarks : IDisposable
{
    private string _databasePath = null!;
    private SqliteDataSource _dataSource = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"tracon-bench-open-{Guid.NewGuid():N}.db");
        _dataSource = new SqliteDataSource($"Data Source={_databasePath};Pooling=True");

        // The first open creates the native connection and registers the functions on it; the benchmark is every open after.
        var first = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);
        await first.DisposeAsync().ConfigureAwait(false);
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    /// <summary>Satisfies CA1001 (the class owns a data source and a file); BenchmarkDotNet itself calls <see cref="Cleanup"/>, not this.</summary>
    public void Dispose()
    {
        _dataSource?.Dispose();
        SqliteConnection.ClearAllPools();

        if (_databasePath is not null)
        {
            foreach (var file in Directory.GetFiles(Path.GetDirectoryName(_databasePath)!, Path.GetFileName(_databasePath) + "*"))
            {
                File.Delete(file);
            }
        }

        GC.SuppressFinalize(this);
    }

    [Benchmark]
    public async Task PooledOpen()
    {
        var connection = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);
        await connection.DisposeAsync().ConfigureAwait(false);
    }
}
