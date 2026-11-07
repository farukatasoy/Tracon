using System.Collections.Concurrent;

namespace Tracon.Tests.Common;

/// <summary>
/// Two races on the start of a run, written once for every SQL provider: a queued run is started a second time while an event
/// is appended to it and the run is read, and several writers start one new run at the same moment.
/// </summary>
/// <remarks>
/// <para>
/// A queued run is written twice: the placeholder, then the worker that runs it, which also sets the time the run started.
/// Whatever the provider does for that second write (move the row, write a new version of it), the calls that overlap it must
/// still see the run: an append may not fail, and a read may not find nothing. The race is not deterministic, so each round
/// is one start against several appends and several reads released at the same moment, and the caller runs many rounds.
/// </para>
/// <para>
/// The first write of a run takes the other path of the same statement: no row exists, so the update finds nothing and the
/// insert follows. Writers that start one new run together must all succeed and leave one run (<see cref="RunFirstWriteAsync"/>).
/// </para>
/// </remarks>
internal static class RunStartRace
{
    /// <summary>
    /// Runs <paramref name="rounds"/> rounds of one second start, <paramref name="appenders"/> appends and
    /// <paramref name="readers"/> reads of one run.
    /// </summary>
    /// <param name="runs">The run store of one migrated schema, over the tenant the caller set.</param>
    /// <param name="rounds">How many runs are raced, one after the other.</param>
    /// <param name="appenders">How many events are appended to a run at the same moment as its second start.</param>
    /// <param name="readers">How many reads of the run start at the same moment.</param>
    /// <returns>One line per call that failed or found no run; empty when every call finished.</returns>
    public static async Task<IReadOnlyCollection<string>> RunAsync(IRunStore runs, int rounds, int appenders, int readers)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var failures = new ConcurrentBag<string>();

        for (var round = 0; round < rounds; round++)
        {
            var id = Guid.NewGuid();
            var queuedAt = DateTimeOffset.UtcNow;
            var info = new RunStartInfo { RunId = id, AgentName = "agent", StartedAt = queuedAt, Status = RunStatus.Queued };

            await runs.StartRunAsync(info);

            using var barrier = new Barrier(1 + appenders + readers);
            var racers = new List<Task>
            {
                Race(barrier, failures, "start", () => runs.StartRunAsync(info with { Status = RunStatus.Running, StartedAt = queuedAt.AddSeconds(5) }).AsTask()),
            };

            for (var sequence = 0; sequence < appenders; sequence++)
            {
                var number = sequence;

                racers.Add(Race(
                    barrier,
                    failures,
                    "append",
                    () => runs.AppendEventAsync(new RunEvent { RunId = id, Sequence = number, Type = RunEventType.MessageDelta, Text = "x", Timestamp = DateTimeOffset.UtcNow }).AsTask()));
            }

            for (var reader = 0; reader < readers; reader++)
            {
                racers.Add(Race(
                    barrier,
                    failures,
                    "read",
                    async () =>
                    {
                        // The row exists before the race starts and is never deleted: a read that finds nothing lost it.
                        if (await runs.GetRunAsync(id) is null)
                        {
                            throw new InvalidOperationException("the run was not found while its second start was running");
                        }
                    }));
            }

            await Task.WhenAll(racers);
        }

        return failures;
    }

    /// <summary>
    /// Runs <paramref name="rounds"/> rounds in which <paramref name="writers"/> writers start one new run at the same moment.
    /// </summary>
    /// <param name="runs">The run store of one migrated schema, over the tenant the caller set.</param>
    /// <param name="rounds">How many runs are raced, one after the other.</param>
    /// <param name="writers">How many writers start the same new run.</param>
    /// <returns>One line per call that failed or per run that no read finds afterwards; empty when every call finished.</returns>
    public static async Task<IReadOnlyCollection<string>> RunFirstWriteAsync(IRunStore runs, int rounds, int writers)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var failures = new ConcurrentBag<string>();

        for (var round = 0; round < rounds; round++)
        {
            var id = Guid.NewGuid();
            var startedAt = DateTimeOffset.UtcNow;

            using var barrier = new Barrier(writers);
            var racers = new List<Task>();

            for (var writer = 0; writer < writers; writer++)
            {
                var offset = writer;

                racers.Add(Race(
                    barrier,
                    failures,
                    "first start",
                    () => runs.StartRunAsync(new RunStartInfo { RunId = id, AgentName = "agent", StartedAt = startedAt.AddMilliseconds(offset), Status = RunStatus.Running }).AsTask()));
            }

            await Task.WhenAll(racers);

            if (await runs.GetRunAsync(id) is null)
            {
                failures.Add("read: the run was not found after its writers had finished");
            }
        }

        return failures;
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
