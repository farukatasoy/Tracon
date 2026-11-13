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
/// A listing that overlaps the second start is the third race (<see cref="RunListingAsync"/>): the row moves in the index the
/// list is read from, and a scan that is running when it moves must not return a run twice or skip one that stood still.
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
    /// <param name="eventReaders">How many reads of the run's event stream start at the same moment; none by default.</param>
    /// <returns>One line per call that failed or found no run; empty when every call finished.</returns>
    public static async Task<IReadOnlyCollection<string>> RunAsync(IRunStore runs, int rounds, int appenders, int readers, int eventReaders = 0)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var failures = new ConcurrentBag<string>();

        for (var round = 0; round < rounds; round++)
        {
            var id = Guid.NewGuid();
            var queuedAt = DateTimeOffset.UtcNow;
            var info = new RunStartInfo { RunId = id, AgentName = "agent", StartedAt = queuedAt, Status = RunStatus.Queued };

            await runs.StartRunAsync(info);

            using var barrier = new Barrier(1 + appenders + readers + eventReaders);
            var racers = new List<Task>
            {
                Race(barrier, failures, "start", () => runs.StartRunAsync(info with { Status = RunStatus.Running, StartedAt = queuedAt.AddSeconds(5) }).AsTask()),
            };

            for (var reader = 0; reader < eventReaders; reader++)
            {
                // The stream is read to its end; what it holds depends on how many appends have landed, so only a failure counts.
                racers.Add(Race(
                    barrier,
                    failures,
                    "read events",
                    async () =>
                    {
                        await foreach (var _ in runs.ReadEventsAsync(id))
                        {
                            // Drained.
                        }
                    }));
            }

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

    /// <summary>
    /// Runs <paramref name="rounds"/> rounds in which a queued run is started a second time, which moves its row to the front
    /// of the list, while <paramref name="listers"/> listings of every run are read at the same moment.
    /// </summary>
    /// <param name="runs">The run store of one migrated schema, over the tenant the caller set.</param>
    /// <param name="rounds">How many runs are moved, one after the other. Every run stays, so the list grows by one a round.</param>
    /// <param name="bystanders">How many runs sit in the list, still, before the first round.</param>
    /// <param name="listers">How many listings start at the same moment as the second start.</param>
    /// <returns>One line per listing that returned a run twice, left out a run that did not move, or failed; empty when every listing was whole.</returns>
    /// <remarks>
    /// <para>
    /// Only the run of the round moves. A listing that returns any run twice, or leaves out a run that did not move, has met the
    /// move on its way through the index. A listing takes every run (the page is wider than the list), so the page has no edge
    /// for the moved row to fall over: whatever the order, each run that stood still has to be in it once.
    /// </para>
    /// <para>
    /// The run that moves is not held to that. A list read at <c>READ COMMITTED</c> holds no snapshot: a scan that has passed the
    /// front of the index when the row arrives there, and reaches the old place after the row left it, sees it at neither. On SQL
    /// Server that is what a listing released together with the move does (885 of 900 listings left the moved run out and
    /// none returned a run twice), and the next listing finds it. The move happens once for a run, so only a listing read in
    /// the same instant is affected.
    /// </para>
    /// </remarks>
    public static async Task<IReadOnlyCollection<string>> RunListingAsync(IRunStore runs, int rounds, int bystanders, int listers)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var failures = new ConcurrentBag<string>();
        var existing = new List<Guid>();
        var origin = DateTimeOffset.UtcNow.AddHours(-2);

        for (var bystander = 0; bystander < bystanders; bystander++)
        {
            var id = Guid.NewGuid();

            await runs.StartRunAsync(new RunStartInfo
            {
                RunId = id,
                AgentName = "agent",
                StartedAt = origin.AddSeconds(bystander + 1),
                Status = RunStatus.Running,
            });

            existing.Add(id);
        }

        var query = new RunQuery { Take = bystanders + rounds + 10 };

        for (var round = 0; round < rounds; round++)
        {
            var id = Guid.NewGuid();

            // Queued at the back of the list; started a second time it is at the front.
            var info = new RunStartInfo { RunId = id, AgentName = "agent", StartedAt = origin.AddSeconds(-round - 1), Status = RunStatus.Queued };

            await runs.StartRunAsync(info);
            existing.Add(id);

            var expected = existing.ToHashSet();

            using var barrier = new Barrier(1 + listers);
            var racers = new List<Task>
            {
                Race(
                    barrier,
                    failures,
                    "start",
                    () => runs.StartRunAsync(info with { Status = RunStatus.Running, StartedAt = origin.AddMinutes(1).AddSeconds(round) }).AsTask()),
            };

            for (var lister = 0; lister < listers; lister++)
            {
                racers.Add(Race(
                    barrier,
                    failures,
                    "list",
                    async () =>
                    {
                        var page = await runs.QueryRunsAsync(query);
                        var seen = new HashSet<Guid>();

                        foreach (var record in page)
                        {
                            if (!seen.Add(record.Id))
                            {
                                throw new InvalidOperationException($"run {record.Id} was returned twice while another run moved");
                            }
                        }

                        // The run that moves may be seen at neither place; every other run stands still and has to be there.
                        var missing = expected.Count(known => known != id && !seen.Contains(known));

                        if (missing != 0)
                        {
                            throw new InvalidOperationException($"{missing} of {expected.Count - 1} runs that stood still were left out while another run moved");
                        }
                    }));
            }

            await Task.WhenAll(racers);
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
