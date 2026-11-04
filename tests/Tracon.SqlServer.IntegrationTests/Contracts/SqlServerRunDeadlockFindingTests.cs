using System.Collections.Concurrent;
using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests.Contracts;

/// <summary>
/// A call that overlaps the second start of the same run is not a deadlock victim.
/// </summary>
/// <remarks>
/// <para>
/// A queued run is written twice: the placeholder, then the worker that runs it, which also sets the time the run
/// started. That second write moves the row in the clustered index (<c>runs_cx</c> is keyed on <c>started_at</c>), so it
/// has to rewrite the entry of <c>runs_pk</c>, whose row locator is that clustered key. An event append (its foreign key
/// check) and a read of the run take the shared lock on <c>runs_pk</c> first and the clustered row second. The two orders
/// cross, and the server picks the append or the read as the victim (measured with the <c>system_health</c> deadlock
/// report: 6 victims in 300 rounds, none when the second write left <c>started_at</c> as it was).
/// </para>
/// <para>
/// The race is not deterministic, so the test runs many rounds. The deterministic cases of the retry are in
/// <see cref="StatementDeadlockRetryTests"/>.
/// </para>
/// </remarks>
public sealed class SqlServerRunDeadlockFindingTests(SqlServerSchemaFixture schema) : IClassFixture<SqlServerSchemaFixture>
{
    private const int Rounds = 300;

    [Fact]
    public async Task An_event_append_or_a_read_that_overlaps_a_start_of_the_same_run_is_not_a_deadlock_victim()
    {
        await schema.ResetAsync();
        schema.Tenant.TenantId = "default";
        var runs = schema.Context.Runs;
        var failures = new ConcurrentBag<string>();

        for (var round = 0; round < Rounds; round++)
        {
            var id = Guid.NewGuid();
            var queuedAt = DateTimeOffset.UtcNow;
            var info = new RunStartInfo { RunId = id, AgentName = "agent", StartedAt = queuedAt, Status = RunStatus.Queued };

            await runs.StartRunAsync(info);

            using var barrier = new Barrier(3);
            var start = Race(barrier, failures, "start", () => runs.StartRunAsync(info with { Status = RunStatus.Running, StartedAt = queuedAt.AddSeconds(5) }).AsTask());
            var append = Race(
                barrier,
                failures,
                "append",
                () => runs.AppendEventAsync(new RunEvent { RunId = id, Sequence = 0, Type = RunEventType.MessageDelta, Text = "x", Timestamp = DateTimeOffset.UtcNow }).AsTask());
            var read = Race(barrier, failures, "read", () => runs.GetRunAsync(id).AsTask());

            await Task.WhenAll(start, append, read);
        }

        failures.ShouldBeEmpty("a call failed because it was chosen as a deadlock victim");
    }

    private static Task Race(Barrier barrier, ConcurrentBag<string> failures, string name, Func<Task> call)
        => Task.Factory.StartNew(
            () =>
            {
                barrier.SignalAndWait();

                try
                {
                    call().GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    failures.Add($"{name}: {exception.Message}");
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
}
