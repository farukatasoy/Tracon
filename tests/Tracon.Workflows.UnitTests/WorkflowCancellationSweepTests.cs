using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// Cancels a workflow run at every await point it has, one run per point, and checks the same things each time: every
/// run row reaches a terminal state, the caller sees a cancellation or a result and nothing else, and nothing is left
/// registered, open, armed or holding a slot.
/// </summary>
/// <remarks>
/// <para>
/// The sweep of the agent path (<c>RunCancellationSweepTests</c>) does not reach this runner: a workflow run opens its
/// own row, holds a concurrency slot while the graph runs, writes checkpoints, and closes the row in the <c>finally</c> of
/// an async iterator whose body only runs while somebody enumerates. A first run with no cancellation counts the awaited
/// calls the run makes on its run store, its checkpoint store, its event sink and its agents (one plan numbers them in
/// the order they happen); then run <c>n</c> cancels the run exactly when call <c>n</c> arrives, for <c>n</c> from 1 to
/// that count. Two ways to cancel are swept (the caller's token, and the operator's cancel through the registry) and two
/// kinds of store (one that ignores its token and one that throws when its token is already canceled, as a SQL store
/// does).
/// </para>
/// <para>
/// The oracle is the invariant, not a recorded sequence: no run row left <c>Running</c> or <c>Queued</c> (the root
/// run and the runs of the agents it called), an empty registry, every span the run started stopped, no timer left armed
/// on the runner's clock (the run's deadline is one), and the one concurrency slot free again, which a second run on the
/// same runner proves by completing. The harness is in memory: it has no process, connection or file to leak, and the
/// sweep does not claim to look for any.
/// </para>
/// </remarks>
public sealed class WorkflowCancellationSweepTests
{
    private static readonly TimeSpan RunBound = TimeSpan.FromSeconds(20);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task A_workflow_run_cancelled_at_any_await_point_ends_in_a_terminal_state_and_leaks_nothing(
        bool storeHonorsToken,
        bool operatorCancel)
    {
        var awaitPoints = (await RunAsync(storeHonorsToken, operatorCancel, cancelAt: null)).AwaitPoints;

        awaitPoints.ShouldBeGreaterThan(15, "the run must cross a run store, a checkpoint store, a sink and two agents");

        var problems = new List<string>();

        for (var cancelAt = 1; cancelAt <= awaitPoints; cancelAt++)
        {
            var outcome = await RunAsync(storeHonorsToken, operatorCancel, cancelAt);

            problems.AddRange(outcome.Problems.Select(problem => $"cancel at call {cancelAt} ({outcome.CancelledAt ?? "never reached"}): {problem}"));
        }

        UnobservedTaskGuard.ForceCollection();

        problems.ShouldBeEmpty();
    }

    /// <summary>
    /// The container disposes the runner when the host stops; a run still in flight then gives its slot back to a
    /// semaphore that is gone. The give-back sits in the <c>finally</c> that also closes the run, so an exception from
    /// it used to leave the row at Running and hand the caller an <see cref="ObjectDisposedException"/>.
    /// </summary>
    [Fact]
    public async Task A_runner_disposed_while_a_run_is_in_flight_still_closes_the_run()
    {
        var plan = new FaultPlan();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new WorkflowTestHost();

        host.AddAgent("first", new SweepAgent("first", plan) { Entered = entered, Hold = hold.Task });
        host.AddAgent("second", new SweepAgent("second", plan));

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["first", "second"],
        });

        var catalog = new WorkflowCatalog(
            [],
            host.DefinitionStore,
            host.Compiler,
            host.TenantContext,
            new ServiceCollection().BuildServiceProvider());

        var runner = new WorkflowRunner(
            catalog,
            host.RunStore,
            host.CheckpointStore,
            host.TenantContext,
            Options.Create(new TraconWorkflowOptions { MaxConcurrentRuns = 1 }),
            Options.Create(new TraconOptions()),
            NullLogger<WorkflowRunner>.Instance,
            agentCache: host.AgentCache);

        var running = Task.Run(() => DriveAsync(runner, CancellationToken.None), CancellationToken.None);

        await entered.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

        runner.Dispose();
        hold.SetResult();

        await Should.NotThrowAsync(async () => await running.WaitAsync(RunBound, TestContext.Current.CancellationToken));

        var runs = await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 });

        runs.ShouldNotBeEmpty();
        runs.ShouldAllBe(static run => run.Status != RunStatus.Running && run.Status != RunStatus.Queued, "no run row is left open");
    }

    private static async Task<Outcome> RunAsync(bool storeHonorsToken, bool operatorCancel, int? cancelAt)
    {
        var plan = new FaultPlan { HonorCancellation = storeHonorsToken };
        using var cts = new CancellationTokenSource();
        var registry = new RunCancellationRegistry();
        var clock = new TimerLedgerClock();
        var awaited = 0;
        string? cancelledAt = null;

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
                    registry.TryCancel(runId, "test");
                }
            }
            else
            {
                cts.Cancel();
            }
        };

        var host = new WorkflowTestHost();

        host.AddAgent("first", new SweepAgent("first", plan));
        host.AddAgent("second", new SweepAgent("second", plan));

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["first", "second"],
        });

        var catalog = new WorkflowCatalog(
            [],
            host.DefinitionStore,
            host.Compiler,
            host.TenantContext,
            new ServiceCollection().BuildServiceProvider());

        using var runner = new WorkflowRunner(
            catalog,
            FaultProxy<IRunStore>.Create(host.RunStore, plan),
            FaultProxy<IWorkflowCheckpointStore>.Create(host.CheckpointStore, plan),
            host.TenantContext,
            Options.Create(new TraconWorkflowOptions { MaxConcurrentRuns = 1 }),
            Options.Create(new TraconOptions()),
            NullLogger<WorkflowRunner>.Instance,
            timeProvider: clock,
            cancellationRegistry: registry,
            sinks: [FaultProxy<IRunEventSink>.Create(new NullSink(), plan)],
            agentCache: host.AgentCache);

        using var activities = new ActivityCounter();

        Exception? failure = null;

        try
        {
            await Task.Run(() => DriveAsync(runner, cts.Token), CancellationToken.None).WaitAsync(RunBound, CancellationToken.None);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        // The calls of the run itself: the second run below is not part of the count, and the plan stops hooking.
        var awaitPoints = Volatile.Read(ref awaited);
        plan.OnCall = null;
        plan.HonorCancellation = false;

        var problems = new List<string>();

        if (failure is TimeoutException)
        {
            problems.Add($"the run did not finish within {RunBound.TotalSeconds:0} s (I-03/I-08); the calls it had made: {string.Join(", ", plan.Calls)}");
        }
        else if (failure is not null && failure is not OperationCanceledException)
        {
            problems.Add($"the caller got {failure.GetType().Name}: {failure.Message}");
        }

        var runs = await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 });

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

        if (clock.Armed != 0)
        {
            problems.Add($"{clock.Armed} timer(s) are still armed on the runner's clock, made at: {clock.Origins}");
        }

        if (failure is not TimeoutException)
        {
            // The one slot is free again: a second run on the same runner finishes. A slot a cancelled run kept
            // would hold this one back until the bound.
            try
            {
                await Task.Run(() => DriveAsync(runner, CancellationToken.None), CancellationToken.None).WaitAsync(RunBound, CancellationToken.None);
            }
            catch (Exception exception)
            {
                problems.Add($"a run after the cancelled one did not complete: {exception.GetType().Name}");
            }
        }

        return new Outcome(awaitPoints, cancelledAt, problems);
    }

    private static async Task DriveAsync(WorkflowRunner runner, CancellationToken cancellationToken)
    {
        await foreach (var _ in runner.RunStreamingAsync(
            new WorkflowRunRequest { WorkflowName = "chain", Message = "hello" },
            cancellationToken))
        {
            // Events are drained; the sweep looks at the terminal state.
        }
    }

    private sealed record Outcome(int AwaitPoints, string? CancelledAt, List<string> Problems);

    private sealed class NullSink : IRunEventSink
    {
        public ValueTask OnEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// Counts the activities of the Tracon source that started and did not stop, among those of one trace: the test
    /// opens a root activity of its own and the run's spans become its children. Other tests of the process start
    /// activities on the same source at the same time; a count of the whole source would see theirs.
    /// </summary>
    private sealed class ActivityCounter : IDisposable
    {
        private const string SourceName = "Tracon.Tests.WorkflowCancellationSweep";

        private static readonly ActivitySource Source = new(SourceName);

        private readonly ActivityListener _listener;
        private readonly Activity _root;
        private int _open;

        public ActivityCounter()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = static source => string.Equals(source.Name, TraconDiagnostics.ActivitySourceName, StringComparison.Ordinal)
                    || string.Equals(source.Name, SourceName, StringComparison.Ordinal),
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = OnStarted,
                ActivityStopped = OnStopped,
            };

            ActivitySource.AddActivityListener(_listener);

            _root = Source.StartActivity("sweep", ActivityKind.Internal, parentContext: default)
                ?? throw new InvalidOperationException("The test activity source has no listener.");
        }

        public int Open => Volatile.Read(ref _open);

        public void Dispose()
        {
            _root.Dispose();
            _listener.Dispose();
        }

        private bool IsRunSpan(Activity activity)
            => _root is not null && !ReferenceEquals(activity, _root) && activity.TraceId == _root.TraceId;

        private void OnStarted(Activity activity)
        {
            if (IsRunSpan(activity))
            {
                Interlocked.Increment(ref _open);
            }
        }

        private void OnStopped(Activity activity)
        {
            if (IsRunSpan(activity))
            {
                Interlocked.Decrement(ref _open);
            }
        }
    }

    /// <summary>
    /// The runner's clock. Time is the system's; a timer is not. A short timer (the pause before the closing write is
    /// asked again is one) fires at once, so no run waits for real time; a long one (the run's deadline) never fires
    /// and stays armed until its owner disposes it, which is what <see cref="Armed"/> counts: timers made and neither
    /// fired nor disposed.
    /// </summary>
    private sealed class TimerLedgerClock : TimeProvider
    {
        private static readonly TimeSpan ShortTimer = TimeSpan.FromSeconds(1);

        private readonly System.Collections.Concurrent.ConcurrentDictionary<LedgerTimer, string> _live = new();

        /// <summary>Gets how many timers were made and not disposed.</summary>
        public int Armed => _live.Count;

        /// <summary>Gets where the timers that are not disposed were made.</summary>
        public string Origins => string.Join(" || ", _live.Values.Select(static stack => string.Join(" < ", stack
            .Split('\n')
            .Select(static line => line.Trim())
            .Where(static line => line.Contains("Tracon", StringComparison.Ordinal) && !line.Contains("TimerLedgerClock", StringComparison.Ordinal))
            .Take(5))));

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new LedgerTimer(this, callback, state);

            _live[timer] = Environment.StackTrace;
            timer.Change(dueTime, period);

            return timer;
        }

        private sealed class LedgerTimer(TimerLedgerClock owner, TimerCallback callback, object? state) : ITimer
        {
            private int _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    return false;
                }

                if (dueTime >= TimeSpan.Zero && dueTime <= ShortTimer)
                {
                    // A one-shot timer that has fired is no longer armed, whether or not its owner has disposed it yet.
                    _ = Task.Run(
                        () =>
                        {
                            owner._live.TryRemove(this, out _);
                            callback(state);
                        },
                        CancellationToken.None);
                }

                return true;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    owner._live.TryRemove(this, out _);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();

                return default;
            }
        }
    }

    /// <summary>An agent that tags and returns the last message it receives, and makes one awaited call the plan counts.</summary>
    private sealed class SweepAgent(string name, FaultPlan plan) : AIAgent
    {
        private static JsonElement EmptyState { get; } = JsonDocument.Parse("{}").RootElement.Clone();

        /// <summary>Gets a signal the agent sets when its turn starts.</summary>
        public TaskCompletionSource? Entered { get; init; }

        /// <summary>Gets a task the turn waits for before it answers; the turn does not wait when it is <see langword="null"/>.</summary>
        public Task? Hold { get; init; }

        public override string Name => name;

        public override string Description => $"Plays the role of {name}.";

        protected override async Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            await plan.EnterAsync("AgentTurnAsync", cancellationToken).ConfigureAwait(false);
            Entered?.TrySetResult();

            if (Hold is not null)
            {
                await Hold.ConfigureAwait(false);
            }

            await Task.Yield();

            var last = messages.LastOrDefault()?.Text ?? string.Empty;

            return new AgentResponse(new ChatMessage(ChatRole.Assistant, $"[{name}]{last}"));
        }

        protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await RunCoreAsync(messages, session, options, cancellationToken).ConfigureAwait(false);

            foreach (var message in response.Messages)
            {
                yield return new AgentResponseUpdate(message.Role, message.Contents);
            }
        }

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
            => new(new SweepSession());

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            JsonElement serializedState,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => new(new SweepSession());

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
            AgentSession session,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => new(EmptyState);

        private sealed class SweepSession : AgentSession;
    }
}
