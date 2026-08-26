using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;
using Npgsql;

namespace AgentPrism.PostgreSql.IntegrationTests;

/// <summary>
/// The 110.1 measurement: does an <c>NpgsqlDataSource</c> built from the SAME
/// connection string as another <c>NpgsqlDataSource</c> share ITS connection
/// pool, or open a second one?
/// </summary>
/// <remarks>
/// <para>
/// This settles a documented contradiction (measured 2026-08-26): the
/// embedding guide claimed "the two planes share one Npgsql connection pool";
/// the feasibility report's section 7.1 claimed the opposite ("the pool
/// doubles"). Both cannot be true. The result is written to the phase
/// document's "Plandan Sapmalar" section, not guessed.
/// </para>
/// <para>
/// Mechanism: since Npgsql 7.0, connection pooling is scoped to the
/// <see cref="NpgsqlDataSource"/> INSTANCE, not to the connection string
/// globally (that was the legacy <c>NpgsqlConnection</c>-only pooling model).
/// Two separate <see cref="NpgsqlDataSource"/> objects therefore never share
/// pool state, even when built from an identical connection string.
/// </para>
/// </remarks>
public sealed class ConnectionPoolSharingTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Two_data_sources_built_from_the_same_connection_string_do_not_share_a_pool()
    {
        const int concurrentConnectionsPerSource = 5;
        const string applicationName = "agentprism-pool-sharing-measurement";

        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            ApplicationName = applicationName,
        }.ConnectionString;

        await using var sourceA = new NpgsqlDataSourceBuilder(connectionString).Build();
        await using var sourceB = new NpgsqlDataSourceBuilder(connectionString).Build();

        // Hold N connections open concurrently from EACH data source at once,
        // blocked server-side on pg_sleep, so the backend count reflects
        // distinct server-side connections rather than command execution speed.
        static async Task HoldConnectionAsync(NpgsqlDataSource source, TaskCompletionSource ready)
        {
            await using var connection = await source.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_sleep(3);";

            var executeTask = command.ExecuteNonQueryAsync();
            ready.TrySetResult();
            await executeTask;
        }

        var readySignals = Enumerable.Range(0, concurrentConnectionsPerSource * 2)
            .Select(static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();

        var holdTasks = new Task[concurrentConnectionsPerSource * 2];

        for (var i = 0; i < concurrentConnectionsPerSource; i++)
        {
            holdTasks[i * 2] = HoldConnectionAsync(sourceA, readySignals[i * 2]);
            holdTasks[(i * 2) + 1] = HoldConnectionAsync(sourceB, readySignals[(i * 2) + 1]);
        }

        // Wait for every ExecuteNonQueryAsync call to have actually been
        // issued (not merely for OpenConnectionAsync) before counting
        // backends, so the count reflects connections that are really live
        // on the server rather than ones still being established.
        await Task.WhenAll(readySignals.Select(static signal => signal.Task));

        await using var admin = new NpgsqlConnection(fixture.ConnectionString);
        await admin.OpenAsync();

        await using var countCommand = admin.CreateCommand();
        countCommand.CommandText =
            "SELECT count(*) FROM pg_stat_activity WHERE application_name = @app;";
        countCommand.Parameters.AddWithValue("app", applicationName);

        var backendCount = Convert.ToInt32(
            await countCommand.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);

        await Task.WhenAll(holdTasks);

        // If the two data sources shared ONE pool, at most
        // `concurrentConnectionsPerSource` backends would ever be open at
        // once (the pool would hand out the same underlying connections to
        // both). They do not: close to 2 * concurrentConnectionsPerSource
        // backends are open simultaneously, proving each NpgsqlDataSource
        // keeps its OWN pool even with an identical connection string.
        backendCount.ShouldBeGreaterThan(concurrentConnectionsPerSource);
    }
}
