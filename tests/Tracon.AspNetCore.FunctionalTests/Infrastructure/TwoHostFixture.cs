using Microsoft.Extensions.DependencyInjection;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Two real <see cref="TraconTestHost"/>s that share one SQLite file: the shape of a
/// two-replica deployment.
/// </summary>
/// <remarks>
/// <para>
/// Each host has its own DI container, so its own <see cref="IRunCancellationRegistry"/>,
/// its own heartbeat writer and its own job worker. What lives in the process of one
/// replica stays there; only the database is shared. Two in-memory hosts would each get a
/// private store and prove nothing (the reason <c>McpTaskCrossInstanceTests</c> uses a
/// file).
/// </para>
/// <para>
/// A reconciliation interval of 200 ms stands in for the 30 s default. The default is
/// measured by the manual acceptance case, not here.
/// </para>
/// </remarks>
internal sealed class TwoHostFixture : IAsyncDisposable
{
    /// <summary>The heartbeat interval the hosts use when reconciliation is on.</summary>
    public static readonly TimeSpan TestHeartbeatInterval = TimeSpan.FromMilliseconds(200);

    private readonly TempSqliteDatabase _database;

    private TwoHostFixture(TempSqliteDatabase database, TraconTestHost a, TraconTestHost b)
    {
        _database = database;
        A = a;
        B = b;
    }

    /// <summary>Gets the first replica.</summary>
    public TraconTestHost A { get; }

    /// <summary>Gets the second replica.</summary>
    public TraconTestHost B { get; }

    /// <summary>Starts two hosts on one database.</summary>
    /// <param name="configure">Changes the Tracon chain of BOTH hosts, after the database is chosen.</param>
    /// <param name="configureA">Changes the chain of the first host only.</param>
    /// <param name="configureB">Changes the chain of the second host only.</param>
    /// <param name="reconciliation">
    /// Turns <c>Tracon:RunReconciliation</c> on for both hosts with a heartbeat of
    /// <see cref="TestHeartbeatInterval"/>. Off by default, as in production.
    /// </param>
    /// <param name="configureServices">Registers additional services in BOTH hosts.</param>
    /// <returns>The running pair.</returns>
    public static async Task<TwoHostFixture> StartAsync(
        Action<ITraconBuilder>? configure = null,
        Action<ITraconBuilder>? configureA = null,
        Action<ITraconBuilder>? configureB = null,
        bool reconciliation = false,
        Action<IServiceCollection>? configureServices = null)
    {
        var database = new TempSqliteDatabase("two-host");
        TraconTestHost? a = null;

        try
        {
            a = await StartOneAsync(database, configure, configureA, reconciliation, configureServices);
            var b = await StartOneAsync(database, configure, configureB, reconciliation, configureServices);

            return new TwoHostFixture(database, a, b);
        }
        catch
        {
            if (a is not null)
            {
                await a.DisposeAsync();
            }

            database.Dispose();

            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await B.DisposeAsync();
        await A.DisposeAsync();
        _database.Dispose();
    }

    private static Task<TraconTestHost> StartOneAsync(
        TempSqliteDatabase database,
        Action<ITraconBuilder>? configure,
        Action<ITraconBuilder>? configureOne,
        bool reconciliation,
        Action<IServiceCollection>? configureServices)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.UseSqlite(database.ConnectionString);
                configure?.Invoke(builder);
                configureOne?.Invoke(builder);
            },
            configureServices: services =>
            {
                if (reconciliation)
                {
                    services.Configure<RunReconciliationOptions>(static options =>
                    {
                        options.Enabled = true;
                        options.HeartbeatInterval = TestHeartbeatInterval;
                    });
                }

                configureServices?.Invoke(services);
            });
}
