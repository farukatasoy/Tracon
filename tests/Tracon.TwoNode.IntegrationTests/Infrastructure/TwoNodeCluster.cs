using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests.Infrastructure;

/// <summary>
/// Two real <see cref="TraconTestHost"/>s that share one SQL database: the shape of a two-replica deployment.
/// </summary>
/// <remarks>
/// <para>
/// Each host has its own DI container, so its own <see cref="IRunCancellationRegistry"/>, its own heartbeat writer, its
/// own job worker and its own MCP tool catalog. What lives in the process of one replica stays there; only the database
/// (and, where a test hands one in, a distributed cache) is shared.
/// </para>
/// <para>
/// A reconciliation interval of 200 ms stands in for the 30 s default. The default is measured by the manual acceptance
/// case, not here.
/// </para>
/// </remarks>
internal sealed class TwoNodeCluster : IAsyncDisposable
{
    /// <summary>The heartbeat interval the hosts use when reconciliation is on.</summary>
    public static readonly TimeSpan TestHeartbeatInterval = TimeSpan.FromMilliseconds(200);

    private TwoNodeCluster(TraconTestHost a, TraconTestHost b)
    {
        A = a;
        B = b;
    }

    /// <summary>Gets the first replica.</summary>
    public TraconTestHost A { get; }

    /// <summary>Gets the second replica.</summary>
    public TraconTestHost B { get; }

    /// <summary>Starts two hosts on one database.</summary>
    /// <param name="database">The schema both hosts connect to.</param>
    /// <param name="configure">Changes the Tracon chain of BOTH hosts, after the database is chosen.</param>
    /// <param name="configureA">Changes the chain of the first host only.</param>
    /// <param name="configureB">Changes the chain of the second host only.</param>
    /// <param name="reconciliation">
    /// Turns <c>Tracon:RunReconciliation</c> on for both hosts with a heartbeat of <see cref="TestHeartbeatInterval"/>.
    /// Off by default, as in production.
    /// </param>
    /// <param name="configureServices">Registers additional services in BOTH hosts.</param>
    /// <returns>The running pair.</returns>
    public static async Task<TwoNodeCluster> StartAsync(
        TwoNodeDatabase database,
        Action<ITraconBuilder>? configure = null,
        Action<ITraconBuilder>? configureA = null,
        Action<ITraconBuilder>? configureB = null,
        bool reconciliation = false,
        Action<IServiceCollection>? configureServices = null)
    {
        ArgumentNullException.ThrowIfNull(database);

        var a = await StartOneAsync(database, configure, configureA, reconciliation, configureServices);

        try
        {
            var b = await StartOneAsync(database, configure, configureB, reconciliation, configureServices);

            return new TwoNodeCluster(a, b);
        }
        catch
        {
            await a.DisposeAsync();

            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await B.DisposeAsync();
        await A.DisposeAsync();
    }

    private static Task<TraconTestHost> StartOneAsync(
        TwoNodeDatabase database,
        Action<ITraconBuilder>? configure,
        Action<ITraconBuilder>? configureOne,
        bool reconciliation,
        Action<IServiceCollection>? configureServices)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                database.Use(builder);
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
