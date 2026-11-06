using System.Data;
using System.Data.Common;
using System.Diagnostics.Metrics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tracon.Tests.Workflows;

namespace Tracon.TwoNode.IntegrationTests.Infrastructure;

/// <summary>
/// The stores of one schema on a database server, as a cancellation sweep of the workflow runner runs against them: the
/// stores a host gets from <c>UsePostgreSql()</c> or <c>UseSqlServer()</c>, a data source the sweep can read the pool of,
/// and a look at the server for what a canceled run might have left in it.
/// </summary>
/// <remarks>
/// <para>
/// All the runs of a sweep share the schema, migrated once, and are told apart by tenant. No host is started: the stores
/// come out of a container of services that never runs a hosted service, so nothing else writes to the schema while the
/// sweep looks at its rows.
/// </para>
/// <para>
/// Two things are looked for after every run. The pool: a connection that was checked out and not given back. The
/// server: a session that is still inside a transaction or running a statement, and a lock that is still held. Both are
/// polled for a moment before they count, because the last write of a closing step may still be on its way back to the
/// pool when the stream ends.
/// </para>
/// </remarks>
internal abstract class SqlSweepBackend : IWorkflowSweepBackend
{
    private static readonly TimeSpan LeakGrace = TimeSpan.FromSeconds(5);

    private readonly ServiceProvider _provider;
    private readonly SweepStores _stores;

    /// <summary>Takes the container of services the stores came from.</summary>
    /// <param name="provider">The container; the backend disposes it.</param>
    /// <param name="tenant">The tenant the stores read.</param>
    protected SqlSweepBackend(ServiceProvider provider, SweepTenantContext tenant)
    {
        _provider = provider;
        Tenant = tenant;
        _stores = new SweepStores(
            provider.GetRequiredService<IRunStore>(),
            provider.GetRequiredService<IWorkflowCheckpointStore>(),
            provider.GetRequiredService<IWorkflowRequestStore>(),
            provider.GetRequiredService<IWorkflowResponseStore>());
    }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public SweepTenantContext Tenant { get; }

    /// <inheritdoc />
    public SweepStores OpenStores() => _stores;

    /// <summary>Gets the data source the stores open their connections from.</summary>
    public abstract DbDataSource DataSource { get; }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<string>> FindLeaksAsync() => await FindLeaksAsync(LeakGrace).ConfigureAwait(false);

    /// <summary>Looks for what a finished run left in the pool and on the server, waiting up to <paramref name="grace"/> for it to go.</summary>
    /// <param name="grace">How long a connection, a transaction or a lock may take to go before it counts as left behind.</param>
    /// <returns>One sentence for each thing found; none when both are clean.</returns>
    public async ValueTask<IReadOnlyList<string>> FindLeaksAsync(TimeSpan grace)
    {
        List<string> last = [];

        try
        {
            await WaitUntil.TrueAsync(
                async () =>
                {
                    last = await ProbeAsync().ConfigureAwait(false);

                    return last.Count == 0;
                },
                "the server has no busy connection, open transaction or lock of the sweep left",
                grace).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // The last probe says what is left.
        }

        return last;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync().ConfigureAwait(false);
        await DisposeDataSourceAsync().ConfigureAwait(false);
    }

    /// <summary>Looks at the pool and at the server.</summary>
    /// <returns>One sentence for each thing found; none when both are clean.</returns>
    protected abstract Task<List<string>> ProbeAsync();

    /// <summary>Disposes the data source the stores opened their connections from.</summary>
    /// <returns>A task that completes when it is disposed.</returns>
    protected abstract ValueTask DisposeDataSourceAsync();

    /// <summary>Builds the container of services of a provider and applies its migrations.</summary>
    /// <param name="tenant">The tenant the stores read.</param>
    /// <param name="use">Points the Tracon chain at the schema.</param>
    /// <returns>The container.</returns>
    protected static async Task<ServiceProvider> BuildAsync(SweepTenantContext tenant, Action<ITraconBuilder> use)
    {
        var services = new ServiceCollection();

        services.AddSingleton<ITenantContext>(tenant);
        use(services.AddTracon());

        var provider = services.BuildServiceProvider();

        try
        {
            await provider.GetRequiredService<IMigrationApplier>().ApplyAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await provider.DisposeAsync().ConfigureAwait(false);

            throw;
        }

        return provider;
    }
}

/// <summary>The PostgreSQL schema of a sweep; the pool is Npgsql's own, and the server says what is busy and what is locked.</summary>
internal sealed class PostgreSqlSweepBackend : SqlSweepBackend
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly PoolUsage _pool;
    private readonly string _connectionString;

    private PostgreSqlSweepBackend(ServiceProvider provider, SweepTenantContext tenant, NpgsqlDataSource dataSource, PoolUsage pool, string connectionString)
        : base(provider, tenant)
    {
        _dataSource = dataSource;
        _pool = pool;
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public override DbDataSource DataSource => _dataSource;

    /// <inheritdoc />
    public override string Name => "PostgreSQL";

    /// <summary>Migrates the schema and builds the stores over it.</summary>
    /// <param name="connectionString">The connection string of the login that owns the schema.</param>
    /// <param name="schemaName">The schema.</param>
    /// <returns>The backend.</returns>
    public static async Task<PostgreSqlSweepBackend> CreateAsync(string connectionString, string schemaName)
    {
        // The pool is told apart from every other pool of the process by the name of its data source.
        var poolName = "tracon-sweep-" + Guid.NewGuid().ToString("N");
        var pool = new PoolUsage(poolName);
        var dataSource = new NpgsqlDataSourceBuilder(connectionString) { Name = poolName }.Build();
        var tenant = new SweepTenantContext();

        try
        {
            var provider = await BuildAsync(
                tenant,
                builder => builder.UsePostgreSql(options =>
                {
                    options.DataSource = dataSource;
                    options.SchemaName = schemaName;
                    options.AutoApplyMigrations = false;
                })).ConfigureAwait(false);

            return new PostgreSqlSweepBackend(provider, tenant, dataSource, pool, connectionString);
        }
        catch
        {
            await dataSource.DisposeAsync().ConfigureAwait(false);
            pool.Dispose();

            throw;
        }
    }

    /// <inheritdoc />
    protected override async Task<List<string>> ProbeAsync()
    {
        var problems = new List<string>();

        var used = _pool.Used;

        if (used != 0)
        {
            problems.Add($"{used} pooled connection(s) are checked out and not given back");
        }

        // Another session of this database that runs a statement or sits inside a transaction, and a lock another session
        // of it holds. A pooled connection that is idle holds neither.
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
              (SELECT count(*) FROM pg_stat_activity
                WHERE datname = current_database() AND backend_type = 'client backend' AND pid <> pg_backend_pid()
                  AND state IN ('active', 'idle in transaction', 'idle in transaction (aborted)')),
              (SELECT count(*) FROM pg_locks AS l
                JOIN pg_stat_activity AS s ON s.pid = l.pid
                WHERE l.granted AND s.datname = current_database() AND s.backend_type = 'client backend'
                  AND s.pid <> pg_backend_pid()
                  AND l.locktype IN ('relation', 'tuple', 'page', 'transactionid', 'advisory'))
            """;

        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        if (reader.GetInt64(0) != 0)
        {
            problems.Add($"{reader.GetInt64(0)} session(s) of the database are still running a statement or inside a transaction");
        }

        if (reader.GetInt64(1) != 0)
        {
            problems.Add($"{reader.GetInt64(1)} lock(s) are still held by another session");
        }

        return problems;
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeDataSourceAsync()
    {
        await _dataSource.DisposeAsync().ConfigureAwait(false);
        _pool.Dispose();
    }

    /// <summary>
    /// How many connections of one pool are checked out, read from the metric Npgsql publishes for its pools
    /// (<c>db.client.connection.count</c>, by pool name and state).
    /// </summary>
    private sealed class PoolUsage : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly string _poolName;
        private long _used;

        public PoolUsage(string poolName)
        {
            _poolName = poolName;
            // Npgsql's meter is internal, so there is no instance to target. Another pool of the process publishes the
            // same instrument under its own name; the pool name this class made up is what keeps their numbers out.
            _listener.InstrumentPublished = static (instrument, listener) =>
            {
                if (string.Equals(instrument.Name, "db.client.connection.count", StringComparison.Ordinal))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<int>((_, measurement, tags, _) => Record(measurement, tags));
            _listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) => Record(measurement, tags));
            _listener.Start();
        }

        /// <summary>Gets how many connections of the pool are checked out right now.</summary>
        public long Used
        {
            get
            {
                _listener.RecordObservableInstruments();

                return Volatile.Read(ref _used);
            }
        }

        public void Dispose() => _listener.Dispose();

        private void Record(long measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            string? pool = null;
            string? state = null;

            foreach (var tag in tags)
            {
                if (string.Equals(tag.Key, "db.client.connection.pool.name", StringComparison.Ordinal))
                {
                    pool = tag.Value as string;
                }
                else if (string.Equals(tag.Key, "db.client.connection.state", StringComparison.Ordinal))
                {
                    state = tag.Value as string;
                }
            }

            if (string.Equals(pool, _poolName, StringComparison.Ordinal) && string.Equals(state, "used", StringComparison.Ordinal))
            {
                Volatile.Write(ref _used, measurement);
            }
        }
    }
}

/// <summary>The SQL Server schema of a sweep; a data source counts the connections, and the server says what is open and locked.</summary>
internal sealed class SqlServerSweepBackend : SqlSweepBackend
{
    private readonly CountingDataSource _dataSource;
    private readonly string _connectionString;

    private SqlServerSweepBackend(ServiceProvider provider, SweepTenantContext tenant, CountingDataSource dataSource, string connectionString)
        : base(provider, tenant)
    {
        _dataSource = dataSource;
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public override string Name => "SQL Server";

    /// <inheritdoc />
    public override DbDataSource DataSource => _dataSource;

    /// <summary>Migrates the schema and builds the stores over it.</summary>
    /// <param name="connectionString">The connection string of the login that owns the schema.</param>
    /// <param name="schemaName">The schema.</param>
    /// <returns>The backend.</returns>
    public static async Task<SqlServerSweepBackend> CreateAsync(string connectionString, string schemaName)
    {
        var dataSource = new CountingDataSource(connectionString);
        var tenant = new SweepTenantContext();

        try
        {
            var provider = await BuildAsync(
                tenant,
                builder => builder.UseSqlServer(options =>
                {
                    options.DataSource = dataSource;
                    options.SchemaName = schemaName;
                    options.AutoApplyMigrations = false;
                })).ConfigureAwait(false);

            return new SqlServerSweepBackend(provider, tenant, dataSource, connectionString);
        }
        catch
        {
            dataSource.Dispose();

            throw;
        }
    }

    /// <inheritdoc />
    protected override async Task<List<string>> ProbeAsync()
    {
        var problems = new List<string>();

        if (_dataSource.Open != 0)
        {
            problems.Add($"{_dataSource.Open} SQL Server connection(s) are opened and not closed");
        }

        // Another user session that is inside a transaction, and a lock another user session holds. Every connection
        // holds a shared lock on the database itself, which is not a lock of a run.
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
              (SELECT COUNT(*) FROM sys.dm_exec_sessions
                WHERE is_user_process = 1 AND session_id <> @@SPID AND open_transaction_count > 0),
              (SELECT COUNT(*) FROM sys.dm_tran_locks AS l
                JOIN sys.dm_exec_sessions AS s ON s.session_id = l.request_session_id
                WHERE s.is_user_process = 1 AND l.request_session_id <> @@SPID
                  AND l.request_status = 'GRANT' AND l.resource_type <> 'DATABASE')
            """;

        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        if (reader.GetInt32(0) != 0)
        {
            problems.Add($"{reader.GetInt32(0)} session(s) of the server are still inside a transaction");
        }

        if (reader.GetInt32(1) != 0)
        {
            problems.Add($"{reader.GetInt32(1)} lock(s) are still held by another session");
        }

        return problems;
    }

    /// <inheritdoc />
    protected override ValueTask DisposeDataSourceAsync()
    {
        _dataSource.Dispose();

        return default;
    }

    /// <summary>A data source that builds connections the way the provider's own does and counts those that are open.</summary>
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
            var connection = new SqlConnection(connectionString);
            connection.StateChange += OnStateChange;

            return connection;
        }

        private void OnStateChange(object? sender, StateChangeEventArgs eventArgs)
        {
            if (eventArgs.CurrentState == ConnectionState.Open)
            {
                Interlocked.Increment(ref _open);
            }
            else if (eventArgs.OriginalState == ConnectionState.Open)
            {
                Interlocked.Decrement(ref _open);
            }
        }
    }
}
