using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Tracon.Tests.Workflows;

namespace Tracon.Workflows.UnitTests.Fakes;

/// <summary>
/// The real SQLite stores a cancellation sweep runs against: a database file of its own, the stores a host gets from
/// <c>UseSqlite()</c>, and a data source that counts the connections it hands out.
/// </summary>
/// <remarks>
/// <para>
/// All the runs of a sweep share the file and are told apart by tenant, so the file is migrated once. What a run leaves
/// behind and no row shows is looked for in two places: the data source (a connection opened and neither closed nor
/// disposed) and the database (a write lock or an open transaction, tried from a connection of its own that waits for
/// nothing).
/// </para>
/// <para>
/// The data source is the consumer-supplied seam of <c>UseSqlite()</c>. It sets the same pragmas Tracon's own data source
/// sets on every connection, so the stores behave as they do in a host.
/// </para>
/// </remarks>
internal sealed class SqliteSweepBackend : IWorkflowSweepBackend, IAsyncDisposable
{
    private static readonly TimeSpan LeakGrace = TimeSpan.FromSeconds(3);

    private readonly string _directory;
    private readonly string _file;
    private readonly CountingDataSource _dataSource;
    private readonly ServiceProvider _provider;
    private readonly SweepStores _stores;

    private SqliteSweepBackend(string directory, string file, CountingDataSource dataSource, ServiceProvider provider, SweepTenantContext tenant)
    {
        _directory = directory;
        _file = file;
        _dataSource = dataSource;
        _provider = provider;
        Tenant = tenant;
        _stores = new SweepStores(
            provider.GetRequiredService<IRunStore>(),
            provider.GetRequiredService<IWorkflowCheckpointStore>(),
            provider.GetRequiredService<IWorkflowRequestStore>(),
            provider.GetRequiredService<IWorkflowResponseStore>());
    }

    /// <inheritdoc />
    public string Name => "SQLite";

    /// <inheritdoc />
    public SweepTenantContext Tenant { get; }

    /// <summary>Creates a database file in a directory of its own and applies the migrations.</summary>
    /// <returns>The backend.</returns>
    public static async Task<SqliteSweepBackend> CreateAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tracon-sweep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var file = Path.Combine(directory, "sweep.db");
        var tenant = new SweepTenantContext();
        var dataSource = new CountingDataSource($"Data Source={file}");
        var services = new ServiceCollection();

        services.AddSingleton<ITenantContext>(tenant);
        services.AddTracon().UseSqlite(options =>
        {
            options.DataSource = dataSource;
            options.AutoApplyMigrations = false;
        });

        var provider = services.BuildServiceProvider();

        try
        {
            await provider.GetRequiredService<IMigrationApplier>().ApplyAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await provider.DisposeAsync().ConfigureAwait(false);
            dataSource.Dispose();
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);

            throw;
        }

        return new SqliteSweepBackend(directory, file, dataSource, provider, tenant);
    }

    /// <inheritdoc />
    public SweepStores OpenStores() => _stores;

    /// <summary>Gets the data source the stores of the backend open their connections from.</summary>
    public DbDataSource DataSource => _dataSource;

    /// <summary>Makes a connection to the database file that is neither pooled nor counted, as another process would hold.</summary>
    /// <returns>The closed connection.</returns>
    public SqliteConnection OpenConnectionOutsideTheStores() => new($"Data Source={_file};Pooling=False");

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<string>> FindLeaksAsync() => FindLeaksAsync(LeakGrace);

    /// <summary>Looks for what a finished run left in the database, waiting up to <paramref name="grace"/> for it to go.</summary>
    /// <param name="grace">How long a lock or a connection may take to go before it counts as left behind.</param>
    /// <returns>One sentence for each thing found; none when the database is clean.</returns>
    public async ValueTask<IReadOnlyList<string>> FindLeaksAsync(TimeSpan grace)
    {
        List<string> last = [];

        try
        {
            // The run has ended, but the last write of a closing step may still be on its way out of the pool: a lock
            // that is gone within a moment was not left behind.
            await WaitUntil.TrueAsync(
                () =>
                {
                    last = Probe();

                    return Task.FromResult(last.Count == 0);
                },
                "the database has no open connection and no lock left",
                grace).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // The last probe says what is left.
        }

        return last;
    }

    /// <summary>Closes the stores and deletes the file; a file that cannot be deleted was held by a connection that is still open.</summary>
    /// <returns>A task that completes when the directory is gone.</returns>
    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync().ConfigureAwait(false);
        _dataSource.Dispose();
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    private List<string> Probe()
    {
        var problems = new List<string>();

        if (_dataSource.Open != 0)
        {
            problems.Add($"{_dataSource.Open} SQLite connection(s) are still open");
        }

        try
        {
            // A connection of its own, outside the pool and outside the counting data source, that waits for next to
            // nothing: if it cannot take the write lock, somebody holds it, and if the log cannot be truncated, a reader
            // still has it. (The provider sends a refused statement again until the command's timeout has passed, and a
            // timeout of zero means it never stops; one second is the shortest wait there is.)
            using var probe = new SqliteConnection($"Data Source={_file};Pooling=False;Default Timeout=1");
            probe.Open();

            using (var timeout = probe.CreateCommand())
            {
                timeout.CommandText = "PRAGMA busy_timeout = 0;";
                timeout.ExecuteNonQuery();
            }

            using (var begin = probe.CreateCommand())
            {
                begin.CommandText = "BEGIN IMMEDIATE; ROLLBACK;";
                begin.ExecuteNonQuery();
            }

            using var checkpoint = probe.CreateCommand();
            checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";

            using var reader = checkpoint.ExecuteReader();

            if (reader.Read() && reader.GetInt32(0) != 0)
            {
                problems.Add("the write-ahead log cannot be truncated: a connection still reads or writes");
            }
        }
        catch (SqliteException exception)
        {
            problems.Add($"the database is locked: {exception.Message}");
        }

        return problems;
    }

    /// <summary>A data source that builds connections the way Tracon's own does and counts those that are open.</summary>
    private sealed class CountingDataSource(string connectionString) : DbDataSource
    {
        private int _open;

        /// <summary>Gets how many connections were opened and neither closed nor disposed.</summary>
        public int Open => Volatile.Read(ref _open);

        /// <inheritdoc />
        public override string ConnectionString => connectionString;

        /// <inheritdoc />
        protected override DbConnection CreateDbConnection()
        {
            var connection = new SqliteConnection(connectionString);
            connection.StateChange += OnStateChange;

            return connection;
        }

        private void OnStateChange(object? sender, StateChangeEventArgs eventArgs)
        {
            if (sender is not SqliteConnection connection)
            {
                return;
            }

            if (eventArgs.CurrentState == ConnectionState.Open)
            {
                Interlocked.Increment(ref _open);

                using var command = connection.CreateCommand();
                command.CommandText =
                    "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 5000; PRAGMA case_sensitive_like = ON;";
                command.ExecuteNonQuery();
            }
            else if (eventArgs.OriginalState == ConnectionState.Open)
            {
                Interlocked.Decrement(ref _open);
            }
        }
    }
}
