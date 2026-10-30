using System.Collections.Concurrent;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// One tenant cannot take every workflow slot of a process, and a run that waits for a slot
/// says so.
/// </summary>
/// <remarks>
/// <para>
/// The cases run two tenants through ONE runner, because the slots belong to the runner. The tenant of a
/// run is the ambient one, so each run is started inside <see cref="WorkflowTestHost.FixedTenantContext.Scope"/>.
/// </para>
/// <para>
/// A run is held inside its function node until the test lets it go, so "waiting" and "running" are
/// states the test observes (the node was entered, or it was not) and not guesses about timing. A
/// negative claim ("this run has not started") is only made at a moment a positive signal proves the
/// slot was already contested: another run entering, or the waiting row reading <c>Queued</c>.
/// </para>
/// </remarks>
public sealed class TenantWorkflowConcurrencyTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task A_tenant_at_its_limit_waits_Queued_and_leaves_the_shared_slot_to_another_tenant()
    {
        var (host, gate) = await BuildAsync();
        var runner = host.CreateRunner(static options =>
        {
            options.MaxConcurrentRuns = 2;
            options.MaxConcurrentRunsPerTenant = 1;
        });

        var first = Start(runner, "tenant-a");
        await gate.EnteredAsync(await first.RunId).WaitAsync(Patience, TestContext.Current.CancellationToken);

        var second = Start(runner, "tenant-a");
        var secondId = await second.RunId.WaitAsync(Patience, TestContext.Current.CancellationToken);

        // The second run of the tenant has its row and is waiting for the tenant's slot.
        (await StatusAsync(host, "tenant-a", secondId)).ShouldBe(RunStatus.Queued);

        // Two shared slots, one held: had the waiting run taken the other one, this tenant's run could not start.
        var other = Start(runner, "tenant-b");
        var otherId = await other.RunId.WaitAsync(Patience, TestContext.Current.CancellationToken);
        await gate.EnteredAsync(otherId).WaitAsync(Patience, TestContext.Current.CancellationToken);

        (await StatusAsync(host, "tenant-b", otherId)).ShouldBe(RunStatus.Running);
        gate.HasEntered(secondId).ShouldBeFalse();
        (await StatusAsync(host, "tenant-a", secondId)).ShouldBe(RunStatus.Queued);

        // The tenant's first run ends: the waiting one takes the slot, and its row says so.
        gate.Release(await first.RunId);
        await gate.EnteredAsync(secondId).WaitAsync(Patience, TestContext.Current.CancellationToken);

        (await StatusAsync(host, "tenant-a", secondId)).ShouldBe(RunStatus.Running);

        gate.ReleaseAll();
        await Task.WhenAll(first.Events, second.Events, other.Events).WaitAsync(Patience, TestContext.Current.CancellationToken);

        (await StatusAsync(host, "tenant-a", secondId)).ShouldBe(RunStatus.Completed);
        (await StatusAsync(host, "tenant-b", otherId)).ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task Without_a_tenant_limit_one_tenant_fills_the_shared_slots_and_the_next_run_waits_Queued()
    {
        var (host, gate) = await BuildAsync();
        var runner = host.CreateRunner(static options => options.MaxConcurrentRuns = 2);

        var first = Start(runner, "tenant-a");
        var second = Start(runner, "tenant-a");
        await gate.EnteredAsync(await first.RunId).WaitAsync(Patience, TestContext.Current.CancellationToken);
        await gate.EnteredAsync(await second.RunId).WaitAsync(Patience, TestContext.Current.CancellationToken);

        var other = Start(runner, "tenant-b");
        var otherId = await other.RunId.WaitAsync(Patience, TestContext.Current.CancellationToken);

        // The shared limit is the only one: the other tenant waits, and it shows. Before this change the
        // row read Running while the run waited.
        (await StatusAsync(host, "tenant-b", otherId)).ShouldBe(RunStatus.Queued);
        gate.HasEntered(otherId).ShouldBeFalse();

        gate.Release(await first.RunId);
        await gate.EnteredAsync(otherId).WaitAsync(Patience, TestContext.Current.CancellationToken);

        (await StatusAsync(host, "tenant-b", otherId)).ShouldBe(RunStatus.Running);

        gate.ReleaseAll();
        await Task.WhenAll(first.Events, second.Events, other.Events).WaitAsync(Patience, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_run_that_finds_a_free_slot_opens_Running_at_once()
    {
        var (host, gate) = await BuildAsync();
        var runner = host.CreateRunner(static options =>
        {
            options.MaxConcurrentRuns = 4;
            options.MaxConcurrentRunsPerTenant = 2;
        });

        var run = Start(runner, "tenant-a");
        var runId = await run.RunId.WaitAsync(Patience, TestContext.Current.CancellationToken);
        await gate.EnteredAsync(runId).WaitAsync(Patience, TestContext.Current.CancellationToken);

        (await StatusAsync(host, "tenant-a", runId)).ShouldBe(RunStatus.Running);

        gate.ReleaseAll();
        await run.Events.WaitAsync(Patience, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_waiting_run_that_is_canceled_closes_Canceled_and_the_tenant_keeps_its_limit()
    {
        var (host, gate) = await BuildAsync();
        var runner = host.CreateRunner(static options =>
        {
            options.MaxConcurrentRuns = 2;
            options.MaxConcurrentRunsPerTenant = 1;
        });

        var holder = Start(runner, "tenant-a");
        var holderId = await holder.RunId;
        await gate.EnteredAsync(holderId).WaitAsync(Patience, TestContext.Current.CancellationToken);

        using var cancel = new CancellationTokenSource();
        var waiting = Start(runner, "tenant-a", cancel.Token);
        var waitingId = await waiting.RunId.WaitAsync(Patience, TestContext.Current.CancellationToken);

        (await StatusAsync(host, "tenant-a", waitingId)).ShouldBe(RunStatus.Queued);

        await cancel.CancelAsync();
        await waiting.Events.WaitAsync(Patience, TestContext.Current.CancellationToken);

        (await StatusAsync(host, "tenant-a", waitingId)).ShouldBe(RunStatus.Canceled);

        // The canceled run held nothing, so it gave nothing back: the tenant is still at its limit of one,
        // and a new run of the tenant waits behind the holder.
        var next = Start(runner, "tenant-a");
        var nextId = await next.RunId.WaitAsync(Patience, TestContext.Current.CancellationToken);

        (await StatusAsync(host, "tenant-a", nextId)).ShouldBe(RunStatus.Queued);
        gate.HasEntered(nextId).ShouldBeFalse();

        // And the slot it never had is not lost either: when the holder ends, the next run takes it.
        gate.Release(holderId);
        await gate.EnteredAsync(nextId).WaitAsync(Patience, TestContext.Current.CancellationToken);

        gate.ReleaseAll();
        await Task.WhenAll(holder.Events, next.Events).WaitAsync(Patience, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_run_that_times_out_waiting_for_its_tenant_slot_names_the_setting_and_leaks_nothing()
    {
        var (host, gate) = await BuildAsync();
        var clock = new TriggerableTimeProvider();
        var runner = host.CreateRunnerWithTimeProvider(
            clock,
            static options =>
            {
                options.MaxConcurrentRuns = 2;
                options.MaxConcurrentRunsPerTenant = 1;
                options.RunTimeout = TimeSpan.FromMinutes(5);
            });

        var holder = Start(runner, "tenant-a");
        var holderId = await holder.RunId;
        await gate.EnteredAsync(holderId).WaitAsync(Patience, TestContext.Current.CancellationToken);

        var waiting = Start(runner, "tenant-a");
        var waitingId = await waiting.RunId.WaitAsync(Patience, TestContext.Current.CancellationToken);

        (await StatusAsync(host, "tenant-a", waitingId)).ShouldBe(RunStatus.Queued);

        clock.TriggerAll();

        var events = await waiting.Events.WaitAsync(Patience, TestContext.Current.CancellationToken);

        events[^1].Type.ShouldBe(RunEventType.RunFailed);
        events[^1].Text.ShouldNotBeNull().ShouldContain("Tracon:Workflows:RunTimeout");

        var closed = (await RunAsync(host, "tenant-a", waitingId)).ShouldNotBeNull();

        closed.Status.ShouldBe(RunStatus.Canceled);
        closed.Error.ShouldNotBeNull().Type.ShouldBe(nameof(TimeoutException));

        gate.Release(holderId);
        await holder.Events.WaitAsync(Patience, TestContext.Current.CancellationToken);

        // Both slots came back: a later run of the tenant starts at once.
        var later = Start(runner, "tenant-a");
        await gate.EnteredAsync(await later.RunId).WaitAsync(Patience, TestContext.Current.CancellationToken);

        gate.ReleaseAll();
        await later.Events.WaitAsync(Patience, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_failing_store_write_when_the_run_leaves_the_queue_does_not_stop_the_run()
    {
        var (host, gate) = await BuildAsync();
        var store = new PromotionRefusingRunStore(host.RunStore);
        var runner = host.CreateRunnerWith(
            runStore: store,
            configure: static options =>
            {
                options.MaxConcurrentRuns = 2;
                options.MaxConcurrentRunsPerTenant = 1;
            });

        var holder = Start(runner, "tenant-a");
        var holderId = await holder.RunId;
        await gate.EnteredAsync(holderId).WaitAsync(Patience, TestContext.Current.CancellationToken);

        var waiting = Start(runner, "tenant-a");
        var waitingId = await waiting.RunId.WaitAsync(Patience, TestContext.Current.CancellationToken);

        store.RefusePromotions = true;
        gate.Release(holderId);
        await gate.EnteredAsync(waitingId).WaitAsync(Patience, TestContext.Current.CancellationToken);

        // The write that would have said Running was refused; the run goes on, and its closing write lands.
        gate.ReleaseAll();
        await Task.WhenAll(holder.Events, waiting.Events).WaitAsync(Patience, TestContext.Current.CancellationToken);

        store.PromotionsRefused.ShouldBe(1);
        (await RunAsync(host, "tenant-a", waitingId)).ShouldNotBeNull().Status.ShouldBe(RunStatus.Completed);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task A_run_that_waits_for_a_slot_signals_at_once_only_while_run_reconciliation_is_on(bool reconciliation, bool closable)
    {
        // Reconciliation closes a Queued row only when it has a heartbeat. A run that waits in its process writes one
        // as it opens, not at the next cycle: a process that stops in between would leave the row Queued for good.
        var (host, gate) = await BuildAsync();
        host.Reconciliation = new RunReconciliationOptions { Enabled = reconciliation };

        var runner = host.CreateRunner(static options =>
        {
            options.MaxConcurrentRuns = 1;
        });

        var holder = Start(runner, "tenant-a");
        var holderId = await holder.RunId;
        await gate.EnteredAsync(holderId).WaitAsync(Patience, TestContext.Current.CancellationToken);

        var waiting = Start(runner, "tenant-a");
        var waitingId = await waiting.RunId.WaitAsync(Patience, TestContext.Current.CancellationToken);

        (await StatusAsync(host, "tenant-a", waitingId)).ShouldBe(RunStatus.Queued);

        // Everything that ever signaled looks stale from an hour ahead.
        var claimed = await host.RunStore.ClaimOrphanedRunsAsync(DateTimeOffset.UtcNow.AddHours(1), 10, TestContext.Current.CancellationToken);

        claimed.Any(run => run.Id == waitingId).ShouldBe(closable);

        gate.ReleaseAll();
        await Task.WhenAll(holder.Events, waiting.Events).WaitAsync(Patience, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_first_heartbeat_the_store_refuses_does_not_stop_the_run()
    {
        var (host, gate) = await BuildAsync();
        var store = new PromotionRefusingRunStore(host.RunStore) { RefuseHeartbeats = true };
        var runner = host.CreateRunnerWith(
            runStore: store,
            configure: static options => options.MaxConcurrentRuns = 1);

        var holder = Start(runner, "tenant-a");
        var holderId = await holder.RunId;
        await gate.EnteredAsync(holderId).WaitAsync(Patience, TestContext.Current.CancellationToken);

        var waiting = Start(runner, "tenant-a");
        var waitingId = await waiting.RunId.WaitAsync(Patience, TestContext.Current.CancellationToken);

        gate.ReleaseAll();
        await Task.WhenAll(holder.Events, waiting.Events).WaitAsync(Patience, TestContext.Current.CancellationToken);

        store.HeartbeatsRefused.ShouldBeGreaterThan(0);
        (await RunAsync(host, "tenant-a", waitingId)).ShouldNotBeNull().Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task Many_runs_of_three_tenants_finish_and_never_pass_either_limit()
    {
        const int tenants = 3;
        const int runsPerTenant = 12;
        const int sharedLimit = 3;
        const int tenantLimit = 2;

        var (host, gate) = await BuildAsync(tenants: ["tenant-a", "tenant-b", "tenant-c"], blocking: false);
        var runner = host.CreateRunner(static options =>
        {
            options.MaxConcurrentRuns = sharedLimit;
            options.MaxConcurrentRunsPerTenant = tenantLimit;
            options.RunTimeout = TimeSpan.FromMinutes(2);
        });

        var runs = new List<Task<List<RunEvent>>>();

        for (var index = 0; index < runsPerTenant; index++)
        {
            for (var tenant = 0; tenant < tenants; tenant++)
            {
                runs.Add(Start(runner, $"tenant-{(char)('a' + tenant)}").Events);
            }
        }

        // Thirty-six runs through three shared slots and two per tenant: every run finishes (no wait cycle) and
        // neither limit is ever passed. What the ORDER of the two slots changes is who waits behind whom: taken
        // the other way round, a tenant's waiters hold shared slots and starve the other tenants - the first case
        // above is the one that tells the orders apart.
        var finished = await Task.WhenAll(runs).WaitAsync(TimeSpan.FromSeconds(90), TestContext.Current.CancellationToken);

        finished.ShouldAllBe(static events => events.Last().Type == RunEventType.RunCompleted);
        gate.PeakShared.ShouldBeLessThanOrEqualTo(sharedLimit);
        gate.PeakPerTenant.Values.ShouldAllBe(peak => peak <= tenantLimit);
        gate.Entries.ShouldBe(tenants * runsPerTenant);
    }

    private static async Task<(WorkflowTestHost Host, SlowNode Gate)> BuildAsync(
        string[]? tenants = null,
        bool blocking = true)
    {
        var host = new WorkflowTestHost("one");
        var gate = new SlowNode(blocking);

        host.AddFunction<List<ChatMessage>, List<ChatMessage>>("slow", gate.RunAsync);

        foreach (var tenant in tenants ?? ["tenant-a", "tenant-b"])
        {
            using (WorkflowTestHost.FixedTenantContext.Scope(tenant))
            {
                await host.SaveAsync(new WorkflowDefinition
                {
                    Name = "chain",
                    Kind = WorkflowKind.Sequential,
                    Nodes =
                    [
                        new WorkflowNodeReference { Name = "one", Kind = WorkflowNodeKind.Agent },
                        new WorkflowNodeReference { Name = "slow", Kind = WorkflowNodeKind.Function },
                    ],
                });
            }
        }

        return (host, gate);
    }

    /// <summary>Starts a run of <paramref name="tenant"/>; the run is that tenant's for its whole life.</summary>
    private static StartedRun Start(WorkflowRunner runner, string tenant, CancellationToken cancellationToken = default)
    {
        using (WorkflowTestHost.FixedTenantContext.Scope(tenant))
        {
            var started = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);

            return new StartedRun(CollectAsync(runner, started, cancellationToken), started.Task);
        }
    }

    private static async Task<List<RunEvent>> CollectAsync(
        WorkflowRunner runner,
        TaskCompletionSource<Guid> started,
        CancellationToken cancellationToken)
    {
        var events = new List<RunEvent>();

        try
        {
            await foreach (var runEvent in runner.RunStreamingAsync(
                new WorkflowRunRequest { WorkflowName = "chain", Message = "hello" },
                cancellationToken))
            {
                events.Add(runEvent);
                started.TrySetResult(runEvent.RunId);
            }
        }
        catch (OperationCanceledException)
        {
            // A run the caller stopped may end the stream with the exception; the row is what the test reads.
        }
        finally
        {
            started.TrySetCanceled(CancellationToken.None);
        }

        return events;
    }

    private static async Task<RunStatus?> StatusAsync(WorkflowTestHost host, string tenant, Guid runId)
        => (await RunAsync(host, tenant, runId))?.Status;

    private static async Task<RunRecord?> RunAsync(WorkflowTestHost host, string tenant, Guid runId)
    {
        using (WorkflowTestHost.FixedTenantContext.Scope(tenant))
        {
            return await host.RunStore.GetRunAsync(runId, TestContext.Current.CancellationToken);
        }
    }

    private sealed record StartedRun(Task<List<RunEvent>> Events, Task<Guid> RunId);

    /// <summary>The function node every run passes through; it holds the run until the test lets it go.</summary>
    private sealed class SlowNode(bool blocking)
    {
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _entered = new();
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _released = new();
        private readonly Dictionary<string, int> _insidePerTenant = new(StringComparer.Ordinal);
        private readonly Lock _gate = new();
        private volatile bool _releaseAllRuns;
        private int _inside;
        private int _entries;

        public int PeakShared { get; private set; }

        public ConcurrentDictionary<string, int> PeakPerTenant { get; } = new(StringComparer.Ordinal);

        public int Entries => Volatile.Read(ref _entries);

        public Task EnteredAsync(Guid runId) => Signal(_entered, runId).Task;

        public bool HasEntered(Guid runId) => Signal(_entered, runId).Task.IsCompleted;

        public void Release(Guid runId) => Signal(_released, runId).TrySetResult();

        /// <summary>Lets every run through, the ones inside the node now and the ones that reach it later.</summary>
        public void ReleaseAll()
        {
            // The flag first: a run that read it as off is already in _entered, so the loop below frees it.
            _releaseAllRuns = true;

            foreach (var runId in _entered.Keys)
            {
                Signal(_released, runId).TrySetResult();
            }
        }

        public async ValueTask<List<ChatMessage>> RunAsync(List<ChatMessage> messages, IWorkflowContext context, CancellationToken cancellationToken)
        {
            var scope = TraconRunContext.Current!;
            var tenant = scope.TenantId!;

            Enter(tenant);
            Interlocked.Increment(ref _entries);
            Signal(_entered, scope.RootRunId).TrySetResult();

            try
            {
                if (blocking && !_releaseAllRuns)
                {
                    // Ignores its token on purpose, like a node that is busy: a run stopped from outside
                    // keeps the slot until the node returns.
                    await Signal(_released, scope.RootRunId).Task.ConfigureAwait(false);
                }
                else
                {
                    await Task.Delay(Random.Shared.Next(0, 4), CancellationToken.None).ConfigureAwait(false); // delay: simulated
                }

                return messages;
            }
            finally
            {
                Leave(tenant);
            }
        }

        private static TaskCompletionSource Signal(ConcurrentDictionary<Guid, TaskCompletionSource> signals, Guid runId)
            => signals.GetOrAdd(runId, static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        private void Enter(string tenant)
        {
            lock (_gate)
            {
                _inside++;
                PeakShared = Math.Max(PeakShared, _inside);

                var inside = _insidePerTenant.GetValueOrDefault(tenant) + 1;
                _insidePerTenant[tenant] = inside;
                PeakPerTenant.AddOrUpdate(tenant, inside, (_, peak) => Math.Max(peak, inside));
            }
        }

        private void Leave(string tenant)
        {
            lock (_gate)
            {
                _inside--;
                _insidePerTenant[tenant]--;
            }
        }
    }

    /// <summary>A run store that refuses the write which moves a row from Queued to Running, once switched on.</summary>
    private sealed class PromotionRefusingRunStore(IRunStore inner) : IRunStore
    {
        private int _refused;

        private int _heartbeatsRefused;

        public volatile bool RefusePromotions;

        public volatile bool RefuseHeartbeats;

        public int PromotionsRefused => Volatile.Read(ref _refused);

        public int HeartbeatsRefused => Volatile.Read(ref _heartbeatsRefused);

        public ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default)
        {
            if (RefusePromotions && info.Status == RunStatus.Running)
            {
                Interlocked.Increment(ref _refused);

                throw new InvalidOperationException("the run store blinked");
            }

            return inner.StartRunAsync(info, cancellationToken);
        }

        public ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
            => inner.AppendEventAsync(runEvent, cancellationToken);

        public ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken cancellationToken = default)
            => inner.CompleteRunAsync(completion, cancellationToken);

        public ValueTask<long?> GetLastEventSequenceAsync(Guid runId, CancellationToken cancellationToken = default)
            => inner.GetLastEventSequenceAsync(runId, cancellationToken);

        public ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
            => inner.GetRunAsync(runId, cancellationToken);

        public ValueTask<IReadOnlyList<RunRecord>> QueryRunsAsync(RunQuery query, CancellationToken cancellationToken = default)
            => inner.QueryRunsAsync(query, cancellationToken);

        public ValueTask<RunStatistics> GetStatisticsAsync(RunStatisticsQuery query, CancellationToken cancellationToken = default)
            => inner.GetStatisticsAsync(query, cancellationToken);

        public IAsyncEnumerable<RunEvent> ReadEventsAsync(Guid runId, long fromSequence = 0, CancellationToken cancellationToken = default)
            => inner.ReadEventsAsync(runId, fromSequence, cancellationToken);

        public ValueTask RecordToolInvocationAsync(ToolInvocationRecord invocation, CancellationToken cancellationToken = default)
            => inner.RecordToolInvocationAsync(invocation, cancellationToken);

        public ValueTask<bool> CompleteLateToolInvocationAsync(LateToolCompletion completion, CancellationToken cancellationToken = default)
            => inner.CompleteLateToolInvocationAsync(completion, cancellationToken);

        public ValueTask<IReadOnlyList<ToolInvocationRecord>> ListToolInvocationsAsync(Guid runId, CancellationToken cancellationToken = default)
            => inner.ListToolInvocationsAsync(runId, cancellationToken);

        public ValueTask RecordNonTokenUsageAsync(NonTokenUsageRecord record, CancellationToken cancellationToken = default)
            => inner.RecordNonTokenUsageAsync(record, cancellationToken);

        public ValueTask<IReadOnlyList<NonTokenUsageRecord>> ListNonTokenUsageAsync(Guid runId, CancellationToken cancellationToken = default)
            => inner.ListNonTokenUsageAsync(runId, cancellationToken);

        public ValueTask<IReadOnlyList<ToolUsage>> GetToolUsageAsync(ToolUsageQuery query, CancellationToken cancellationToken = default)
            => inner.GetToolUsageAsync(query, cancellationToken);

        public ValueTask<IReadOnlyList<ExperimentVariantResult>> GetExperimentResultsAsync(ExperimentResultsQuery query, CancellationToken cancellationToken = default)
            => inner.GetExperimentResultsAsync(query, cancellationToken);

        public ValueTask<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(RunTimeSeriesQuery query, CancellationToken cancellationToken = default)
            => inner.GetTimeSeriesAsync(query, cancellationToken);

        public ValueTask UpdateRunCostAsync(Guid runId, RunCost? cost, string? tenantId, CancellationToken cancellationToken = default)
            => inner.UpdateRunCostAsync(runId, cost, tenantId, cancellationToken);

        public ValueTask<IReadOnlyList<RunCancellationRequest>> TouchHeartbeatAsync(IReadOnlyCollection<Guid> runIds, DateTimeOffset at, CancellationToken cancellationToken = default)
        {
            if (RefuseHeartbeats)
            {
                Interlocked.Increment(ref _heartbeatsRefused);

                throw new InvalidOperationException("the run store blinked");
            }

            return inner.TouchHeartbeatAsync(runIds, at, cancellationToken);
        }

        public ValueTask<bool> RequestCancellationAsync(
            Guid runId,
            string tenantId,
            DateTimeOffset at,
            CancellationToken cancellationToken = default)
            => inner.RequestCancellationAsync(runId, tenantId, at, cancellationToken);

        public ValueTask<IReadOnlyList<RunRecord>> ClaimOrphanedRunsAsync(DateTimeOffset staleBefore, int max, CancellationToken cancellationToken = default)
            => inner.ClaimOrphanedRunsAsync(staleBefore, max, cancellationToken);
    }
}
