using System.Collections.Concurrent;
using Tracon.SqlServer.IntegrationTests.Infrastructure;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// Groups of events appended to a run while the run is written a second time all land, each exactly once.
/// </summary>
/// <remarks>
/// <para>
/// A queued run is written twice, and an append takes the locks of the run's row in the order the second write can cross.
/// A group is one statement like a single event, so the store sends it again, whole, when the server picks it as a deadlock
/// victim. What would go wrong is the retry of a statement that had already written part of its rows (a group written twice,
/// or half a group), and nothing but the rows can show it: a victim leaves no mark on the result of a call.
/// </para>
/// <para>
/// Each round is one second start against several groups released at the same moment. The groups carry disjoint ranges of
/// sequence numbers; after the round the run holds every number of every group once, in order.
/// </para>
/// </remarks>
public sealed class BatchAppendRaceTests(SqlServerSchemaFixture schema) : IClassFixture<SqlServerSchemaFixture>
{
    private const int Rounds = 200;

    private const int Groups = 4;

    private const int EventsPerGroup = 25;

    [Fact]
    public async Task Groups_appended_while_the_run_is_started_a_second_time_are_each_written_once_and_whole()
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

            using var barrier = new Barrier(1 + Groups);
            var racers = new List<Task>
            {
                Race(barrier, failures, "start", () => runs.StartRunAsync(info with { Status = RunStatus.Running, StartedAt = queuedAt.AddSeconds(5) }).AsTask()),
            };

            for (var group = 0; group < Groups; group++)
            {
                var first = group * EventsPerGroup;
                var events = Enumerable.Range(first, EventsPerGroup).Select(sequence => TestData.Event(id, sequence)).ToArray();

                racers.Add(Race(barrier, failures, $"group {group}", () => runs.AppendEventsAsync(events).AsTask()));
            }

            await Task.WhenAll(racers);

            var sequences = new List<long>();

            await foreach (var runEvent in runs.ReadEventsAsync(id))
            {
                sequences.Add(runEvent.Sequence);
            }

            if (!sequences.SequenceEqual(Enumerable.Range(0, Groups * EventsPerGroup).Select(static n => (long)n)))
            {
                failures.Add($"round {round}: the run holds {sequences.Count} events and not every number once");
            }
        }

        failures.ShouldBeEmpty("a group failed, was written twice, or was written in part");
    }

    private static Task Race(Barrier barrier, ConcurrentBag<string> failures, string name, Func<Task> work)
        => Task.Run(async () =>
        {
            barrier.SignalAndWait();

            try
            {
                await work();
            }
            catch (Exception exception)
            {
                failures.Add($"{name}: {exception.GetType().Name}: {exception.Message}");
            }
        });
}
