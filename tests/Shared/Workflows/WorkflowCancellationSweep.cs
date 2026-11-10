namespace Tracon.Tests.Workflows;

/// <summary>Where a sweep runs: the stores its runs write to, and what the backend can say about what a run left behind.</summary>
/// <remarks>
/// A backend that shares one database between all the runs of a sweep (every SQL backend) separates them by tenant: the
/// sweep sets <see cref="Tenant"/> before it takes the stores of a run, and the stores read it on every call.
/// </remarks>
internal interface IWorkflowSweepBackend : IAsyncDisposable
{
    /// <summary>Gets the name of the backend, for a report.</summary>
    string Name { get; }

    /// <summary>Gets the tenant the stores of the backend read; the sweep gives each of its runs another one.</summary>
    SweepTenantContext Tenant { get; }

    /// <summary>Gets the stores the next run writes to: new ones for an in-memory backend, the same ones for a database.</summary>
    /// <returns>The stores.</returns>
    SweepStores OpenStores();

    /// <summary>
    /// Looks for what a finished run left in the backend that no run row shows: a connection that is still busy, a
    /// transaction that is still open, a lock that is still held.
    /// </summary>
    /// <returns>One sentence for each thing found; none when the backend is clean.</returns>
    ValueTask<IReadOnlyList<string>> FindLeaksAsync();
}

/// <summary>What one sweep found.</summary>
/// <param name="AwaitPoints">How many awaited calls a run of the graph makes; each of them is a run of the sweep.</param>
/// <param name="Endings">How the swept runs ended, by the status of the row of the root run (<c>none</c> when the run was canceled before it opened its row), counted over the runs that cancel a call.</param>
/// <param name="Problems">What the oracle found, one sentence for each; none when the graph holds up.</param>
internal sealed record SweepReport(int AwaitPoints, IReadOnlyDictionary<string, int> Endings, IReadOnlyList<string> Problems);

/// <summary>
/// Cancels a workflow run at every await point it has, one run per point, and checks the same things each time: every
/// run row reaches a terminal state or a state that waits for a human, the caller sees a cancellation or a result and
/// nothing else, and nothing is left registered, open, armed or holding a slot.
/// </summary>
/// <remarks>
/// <para>
/// The sweep of the agent path does not reach this runner: a workflow run opens its own row, holds a concurrency slot
/// while the graph runs, writes checkpoints, and closes the row in the <c>finally</c> of an async iterator whose body only
/// runs while somebody enumerates. A first run with no cancellation counts the awaited calls the run makes on its run
/// store, its checkpoint store, its inbox of requests, its ledger of answers, its event sink and its agents and functions
/// (one plan numbers them in the order they happen); then run <c>n</c> cancels the run exactly when call <c>n</c> arrives,
/// for <c>n</c> from 1 to that count. Two ways to cancel are swept (the caller's token, and the operator's cancel through
/// the registry), and two kinds of store on a backend that can tell them apart (one that ignores its token and one that
/// throws when its token is already canceled, as a SQL store does).
/// </para>
/// <para>
/// A graph can start fresh, or resume: the run that continues a run that waits for an answer once the answer came in has
/// await points of its own (it reads the checkpoint, republishes the request, marks the answer delivered), so the run that
/// waits is driven first, with no cancellation, and the run that answers it is the one the sweep cancels.
/// </para>
/// <para>
/// The oracle is the invariant, not a recorded sequence: no run row left <c>Running</c> or <c>Queued</c> (the root run and
/// the runs of the agents it called; <c>AwaitingInput</c> and <c>AwaitingApproval</c> are places a run rests in, and
/// are fine), an empty registry, every span the run started stopped, no timer left armed on the runner's clock (the run's
/// deadline is one), the one concurrency slot free again, which a second run on the same runner proves by finishing, and
/// whatever the backend itself can say (no busy connection, no open transaction, no lock).
/// </para>
/// <para>
/// Cost, measured on one machine (Apple silicon, release build, the two servers in Docker Desktop). The await points of a
/// run are the same on every backend, because they are the calls the runner makes; a sweep is one run for each of them. The
/// wall time is that of one sweep (one way to cancel) and gives the shortest and the longest of the combinations of the
/// backend. On a database it counts the sweep and nothing else: the schema is migrated before the first run.
/// </para>
/// <list type="table">
/// <listheader><term>Graph and start</term><description>Await points of a run; wall time of a sweep</description></listheader>
/// <item><term>Sequential, fresh</term><description>72; in memory 0.2 to 5.9 s, SQLite 2.2 to 2.3 s, PostgreSQL 3.4 to 4.7 s, SQL Server 14.8 to 17.8 s</description></item>
/// <item><term>FunctionNode, fresh</term><description>49; in memory 0.1 to 0.7 s, SQLite 1.3 to 1.7 s, PostgreSQL 1.9 s, SQL Server 8.5 to 9.9 s</description></item>
/// <item><term>Handoff, fresh</term><description>92; in memory 1.0 to 2.7 s, SQLite 4.0 to 4.1 s, PostgreSQL 3.9 to 5.6 s, SQL Server 23.1 to 25.0 s</description></item>
/// <item><term>AwaitingInput, fresh</term><description>37; in memory 0.1 s, SQLite 0.4 to 0.5 s, PostgreSQL 0.6 s, SQL Server 2.9 to 3.0 s</description></item>
/// <item><term>AwaitingInput, resume</term><description>47; in memory 0.1 s, SQLite 1.0 to 1.4 s, PostgreSQL 2.0 to 2.4 s, SQL Server 5.4 s</description></item>
/// <item><term>ToolApproval, fresh</term><description>46; in memory 0.1 to 0.4 s, SQLite 1.1 s, PostgreSQL 1.3 to 1.6 s, SQL Server 5.5 to 6.1 s</description></item>
/// <item><term>ToolApproval, resume</term><description>82; in memory 1.2 to 2.4 s, SQLite 2.9 to 3.4 s, PostgreSQL 4.9 s, SQL Server 20.1 to 20.4 s</description></item>
/// </list>
/// <para>
/// A whole class takes about 25 s in memory (28 combinations), 30 s on SQLite (14), 47 s on PostgreSQL (14, the container
/// start included) and 3.5 min on SQL Server (14). The spread of one graph in memory is the collector (the sweep forces a
/// collection at its end), not the graph.
/// </para>
/// </remarks>
internal static class WorkflowCancellationSweep
{
    private static readonly TimeSpan RunBound = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How far a timer may be and still fire the moment the runner arms it: the pause before a write is asked again is one,
    /// and the run's deadline, which the run disposes when it ends and nothing ever fires, is not.
    /// </summary>
    private static readonly TimeSpan ShortTimer = TimeSpan.FromSeconds(1);

    /// <summary>Sweeps one combination: the graph, how it starts, the kind of store and the way the run is canceled.</summary>
    /// <param name="backend">Where the runs write.</param>
    /// <param name="graph">The graph to run.</param>
    /// <param name="start">Whether the swept run is a new one or the continuation of a run that waits.</param>
    /// <param name="operatorCancel">Whether the run is canceled through the registry (the operator) and not by its caller's token.</param>
    /// <param name="storeHonorsToken">
    /// Whether every store call throws when its token is already canceled. A backend whose stores do so by themselves
    /// passes <see langword="false"/>: the plan has nothing to add.
    /// </param>
    /// <returns>What the sweep found.</returns>
    public static async Task<SweepReport> SweepAsync(
        IWorkflowSweepBackend backend,
        SweepGraph graph,
        SweepStart start,
        bool operatorCancel,
        bool storeHonorsToken)
    {
        ArgumentNullException.ThrowIfNull(backend);

        var scope = Guid.NewGuid().ToString("N")[..8];
        var problems = new List<string>();

        var first = await RunAsync(backend, graph, start, storeHonorsToken, operatorCancel, cancelAt: null, $"sweep-{scope}-0").ConfigureAwait(false);
        var awaitPoints = first.AwaitPoints;

        problems.AddRange(first.Problems.Select(static problem => $"no cancellation: {problem}"));
        problems.AddRange(first.Missing.Select(static call => $"the run made no {call} call, so the sweep does not reach it"));

        var endings = new SortedDictionary<string, int>(StringComparer.Ordinal);

        for (var cancelAt = 1; cancelAt <= awaitPoints; cancelAt++)
        {
            var outcome = await RunAsync(backend, graph, start, storeHonorsToken, operatorCancel, cancelAt, $"sweep-{scope}-{cancelAt}").ConfigureAwait(false);

            endings[outcome.Ending] = endings.GetValueOrDefault(outcome.Ending) + 1;

            problems.AddRange(outcome.Problems.Select(problem => $"cancel at call {cancelAt} ({outcome.CancelledAt ?? "never reached"}): {problem}"));
        }

        // A sweep whose cancels never land proves nothing: a registry asked for the wrong tenant, or a token nobody
        // listens to, would pass every check above on runs that were never canceled.
        if (!endings.ContainsKey(nameof(RunStatus.Canceled)))
        {
            problems.Add($"no run ended Canceled in {awaitPoints} runs that canceled a call, so the cancels did not land: {string.Join(", ", endings.Select(static ending => $"{ending.Key} {ending.Value}"))}");
        }

        UnobservedTaskGuard.ForceCollection();

        return new SweepReport(awaitPoints, endings, problems);
    }

    /// <summary>Drives a stream of events to its end and drops the events: the sweep looks at the terminal state.</summary>
    /// <param name="events">The stream.</param>
    /// <returns>A task that completes when the stream ends.</returns>
    public static async Task DriveAsync(IAsyncEnumerable<RunEvent> events)
    {
        await foreach (var _ in events.ConfigureAwait(false))
        {
            // Events are drained; the sweep looks at the terminal state.
        }
    }

    private static async Task<Outcome> RunAsync(
        IWorkflowSweepBackend backend,
        SweepGraph graph,
        SweepStart start,
        bool storeHonorsToken,
        bool operatorCancel,
        int? cancelAt,
        string tenantId)
    {
        var plan = new FaultPlan { HonorCancellation = storeHonorsToken };
        using var cts = new CancellationTokenSource();
        var registry = new RunCancellationRegistry();
        // A short pause (a retry, a backoff) passes at once on its own; a long timer (a run's deadline) stays armed to be counted.
        var clock = new ManualClock { AutoFireWithin = ShortTimer, CaptureTimerOrigins = true };
        var awaited = 0;
        string? cancelledAt = null;

        backend.Tenant.TenantId = tenantId;

        var stores = backend.OpenStores();
        var world = await SweepWorld.CreateAsync(graph, plan, stores, backend.Tenant).ConfigureAwait(false);

        if (start == SweepStart.Resume && !world.RestsWaiting)
        {
            throw new ArgumentException($"A run of the {graph} graph never waits for an answer, so there is nothing to resume.", nameof(start));
        }

        using var runner = world.CreateRunner(plan, clock, registry);
        using var activities = new ActivityCounter();

        // The run that answers needs a run that waits, with the question it asks. That run is not swept: the plan hooks
        // nothing yet, and the awaited calls it makes are not counted.
        Func<CancellationToken, IAsyncEnumerable<RunEvent>> swept;
        Guid? answeredRunId = null;

        if (start == SweepStart.Resume)
        {
            var respond = await RestAsync(world, runner, stores).ConfigureAwait(false);

            answeredRunId = respond.RunId;

            swept = token => runner.RespondStreamingAsync(respond, token);
        }
        else
        {
            swept = token => runner.RunStreamingAsync(world.RunRequest, token);
        }

        plan.OnCall = call =>
        {
            if (!call.Method.EndsWith("Async", StringComparison.Ordinal))
            {
                return;
            }

            if (Interlocked.Increment(ref awaited) != cancelAt)
            {
                return;
            }

            cancelledAt = call.Method;

            if (operatorCancel)
            {
                foreach (var runId in registry.ActiveRunIds)
                {
                    registry.TryCancel(runId, tenantId);
                }
            }
            else
            {
                cts.Cancel();
            }
        };

        var callsBefore = plan.Total;
        Exception? failure = null;

        try
        {
            await Task.Run(() => DriveAsync(swept(cts.Token)), CancellationToken.None).WaitAsync(RunBound, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        // The calls of the run itself: the second run below is not part of the count, and the plan stops hooking.
        var awaitPoints = Volatile.Read(ref awaited);
        var calls = plan.Calls.Skip(callsBefore).ToList();
        plan.OnCall = null;
        plan.HonorCancellation = false;

        var problems = new List<string>();

        if (failure is TimeoutException)
        {
            problems.Add($"the run did not finish within {RunBound.TotalSeconds:0} s (I-03/I-08); the calls it had made: {string.Join(", ", calls)}");
        }
        else if (failure is not null && failure is not OperationCanceledException)
        {
            problems.Add($"the caller got {failure.GetType().Name}: {failure.Message}");
        }

        var runs = await stores.Runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }).ConfigureAwait(false);

        // The run that was swept: the root run that is not the one a resume continues.
        var ending = runs
            .Where(run => run.ParentRunId is null && run.Id != answeredRunId)
            .Select(static run => run.Status.ToString())
            .DefaultIfEmpty("none")
            .First();

        foreach (var run in runs.Where(static run => run.Status is RunStatus.Running or RunStatus.Queued))
        {
            problems.Add($"run {run.Id} ({run.Kind}) is still {run.Status} (I-03)");
        }

        if (registry.ActiveCount != 0)
        {
            problems.Add($"{registry.ActiveCount} run(s) are still registered as active");
        }

        if (activities.Open != 0)
        {
            problems.Add($"{activities.Open} activity(ies) started and never stopped");
        }

        if (clock.Pending.Count != 0)
        {
            problems.Add($"{clock.Pending.Count} timer(s) are still armed on the runner's clock, made at: {clock.PendingOrigins}");
        }

        if (failure is not TimeoutException)
        {
            // The one slot is free again: a second run on the same runner finishes. A slot a cancelled run kept
            // would hold this one back until the bound.
            try
            {
                await Task.Run(() => DriveAsync(runner.RunStreamingAsync(world.RunRequest, CancellationToken.None)), CancellationToken.None)
                    .WaitAsync(RunBound, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                problems.Add($"a run after the cancelled one did not complete: {exception.GetType().Name}");
            }
        }

        problems.AddRange(await backend.FindLeaksAsync().ConfigureAwait(false));

        var missing = cancelAt is null
            ? world.ExpectedCalls(start).Where(expected => !calls.Contains(expected, StringComparer.Ordinal)).ToList()
            : [];

        return new Outcome(awaitPoints, cancelledAt, ending, problems, missing);
    }

    /// <summary>Drives a run of the graph to the point where it waits for an answer, and builds the answer.</summary>
    private static async Task<WorkflowRespondRequest> RestAsync(SweepWorld world, WorkflowRunner runner, SweepStores stores)
    {
        await Task.Run(() => DriveAsync(runner.RunStreamingAsync(world.RunRequest, CancellationToken.None)), CancellationToken.None)
            .WaitAsync(RunBound, CancellationToken.None)
            .ConfigureAwait(false);

        var waiting = (await stores.Runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = true, Take = 10 }).ConfigureAwait(false))
            .SingleOrDefault(static run => run.Status == RunStatus.AwaitingInput)
            ?? throw new InvalidOperationException($"The run of the {world.WorkflowName} workflow did not end waiting for input, so there is nothing to answer.");

        var pending = await runner.ListPendingRequestsAsync(waiting.Id).ConfigureAwait(false);
        var request = pending.OrderBy(static candidate => candidate.PortId, StringComparer.Ordinal).FirstOrDefault()
            ?? throw new InvalidOperationException("The run that waits has no request to answer.");

        return new WorkflowRespondRequest { RunId = waiting.Id, RequestId = request.RequestId, Approved = true };
    }

    private sealed record Outcome(int AwaitPoints, string? CancelledAt, string Ending, List<string> Problems, List<string> Missing);
}
