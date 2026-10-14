using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// What the expiry does to a run whose human request ran out of time (phase 209): it closes the
/// run - failed when something went unanswered, completed when everything was answered - and it
/// leaves the run's recorded values as they were.
/// </summary>
/// <remarks>
/// <para>
/// One scan is driven by hand (<see cref="WorkflowRequestExpirationService.TickAsync"/>) over the
/// real engine, the in-memory run store and the in-memory inbox, with a clock the test sets.
/// The row's time is fixed when the run starts waiting, so each case runs the workflow first and
/// moves the clock afterwards.
/// </para>
/// <para>
/// 🚨 <c>IRunStore.CompleteRunAsync</c> REPLACES the event count, usage and cost of the row with
/// what the completion carries. A completion built from nothing erased them: the first draft of
/// the service did exactly that, as <c>ApprovalExpirationService</c> still does, and only a test
/// that reads the row before and after sees it.
/// </para>
/// </remarks>
public sealed class WorkflowRequestExpirationServiceTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromHours(1);

    [Fact]
    public async Task A_run_whose_request_ran_out_of_time_is_closed_failed_with_the_expiry_error()
    {
        var world = new World();
        var run = await world.WaitAsync();

        world.Clock.Advance(Limit + TimeSpan.FromMinutes(1));
        await world.Service.TickAsync(CancellationToken.None);

        var closed = (await world.Host.RunStore.GetRunAsync(run.Id)).ShouldNotBeNull();

        closed.Status.ShouldBe(RunStatus.Failed);
        closed.CompletedAt.ShouldBe(world.Clock.GetUtcNow());

        var error = closed.Error.ShouldNotBeNull();

        error.Type.ShouldBe("WorkflowRequestExpired");
        error.Message.ShouldContain("was not answered and expired at");
        error.Class.ShouldBe(RunErrorClass.Timeout);
        error.Fingerprint.ShouldNotBeNullOrEmpty();

        // The run left AwaitingInput, so the request is no longer listed from the run or the inbox.
        (await world.Runner.ListPendingRequestsAsync(run.Id)).ShouldBeEmpty();
        (await world.Inbox.ListAsync(new WorkflowRequestQuery { State = WorkflowRequestState.All })).ShouldBeEmpty();
        (await world.Inbox.ListByRunAsync(run.Id)).ShouldHaveSingleItem().Status.ShouldBe(WorkflowRequestStatus.Closed);
    }

    [Fact]
    public async Task A_run_is_left_waiting_until_its_time_has_come()
    {
        var world = new World();
        var run = await world.WaitAsync();

        world.Clock.Advance(Limit - TimeSpan.FromMinutes(1));
        await world.Service.TickAsync(CancellationToken.None);

        (await world.Host.RunStore.GetRunAsync(run.Id))!.Status.ShouldBe(RunStatus.AwaitingInput);
        (await world.Inbox.ListByRunAsync(run.Id)).ShouldHaveSingleItem().Status.ShouldBe(WorkflowRequestStatus.Open);
    }

    [Fact]
    public async Task A_run_whose_requests_were_all_answered_is_closed_completed_without_an_error()
    {
        var world = new World();
        var run = await world.WaitAsync();
        var row = (await world.Inbox.ListByRunAsync(run.Id)).Single();

        // The endpoint marks the answer; the answer itself continued on another run (K-130).
        await world.Inbox.MarkAnsweredAsync(row.SessionId, row.RequestId, Guid.NewGuid(), DateTimeOffset.UtcNow);

        world.Clock.Advance(Limit + TimeSpan.FromMinutes(1));
        await world.Service.TickAsync(CancellationToken.None);

        var closed = (await world.Host.RunStore.GetRunAsync(run.Id)).ShouldNotBeNull();

        closed.Status.ShouldBe(RunStatus.Completed);
        closed.Error.ShouldBeNull();
        closed.CompletedAt.ShouldBe(world.Clock.GetUtcNow());
    }

    [Fact]
    public async Task A_run_with_one_request_answered_and_one_not_is_closed_failed()
    {
        var world = new World(TwoQuestionWorkflow.Registration(Limit), TwoQuestionWorkflow.Name);
        var run = await world.WaitAsync();
        var requests = await world.Runner.ListPendingRequestsAsync(run.Id);

        await world.Inbox.MarkAnsweredAsync(run.SessionId!, requests[0].RequestId, Guid.NewGuid(), DateTimeOffset.UtcNow);

        world.Clock.Advance(Limit + TimeSpan.FromMinutes(1));
        await world.Service.TickAsync(CancellationToken.None);

        var closed = (await world.Host.RunStore.GetRunAsync(run.Id)).ShouldNotBeNull();

        closed.Status.ShouldBe(RunStatus.Failed);
        closed.Error.ShouldNotBeNull().Message.ShouldContain(requests[1].RequestId);
    }

    [Fact]
    public async Task The_event_count_of_the_run_survives_the_close()
    {
        var world = new World();
        var run = await world.WaitAsync();
        var before = (await world.Host.RunStore.GetRunAsync(run.Id)).ShouldNotBeNull();

        before.EventCount.ShouldBeGreaterThan(0);

        world.Clock.Advance(Limit + TimeSpan.FromMinutes(1));
        await world.Service.TickAsync(CancellationToken.None);

        var after = (await world.Host.RunStore.GetRunAsync(run.Id)).ShouldNotBeNull();

        after.Status.ShouldBe(RunStatus.Failed);
        after.EventCount.ShouldBe(before.EventCount);
        after.StartedAt.ShouldBe(before.StartedAt);
        after.WorkflowName.ShouldBe(before.WorkflowName);
        after.SessionId.ShouldBe(before.SessionId);
    }

    [Fact]
    public async Task The_usage_cost_and_non_token_cost_of_the_run_survive_the_close()
    {
        var world = new World();
        var run = await world.WaitAsync();

        // What the run recorded before it paused: a workflow run's own row carries the cost of
        // the function nodes it ran, and CompleteRunAsync REPLACES it with what the completion says.
        await world.Host.RunStore.CompleteRunAsync(new RunCompletion
        {
            RunId = run.Id,
            Status = RunStatus.AwaitingInput,
            CompletedAt = run.CompletedAt ?? DateTimeOffset.UtcNow,
            EventCount = 42,
            Usage = new RunUsage { InputTokens = 200, OutputTokens = 100, TotalTokens = 300 },
            Cost = new RunCost { InputCost = 0.5m, OutputCost = 0.25m, Currency = "USD", Source = PricingSource.Catalog },
            NonTokenCost = new RunNonTokenCost { Amount = 1.75m, Currency = "USD" },
            TenantId = world.Host.TenantContext.TenantId,
        });

        world.Clock.Advance(Limit + TimeSpan.FromMinutes(1));
        await world.Service.TickAsync(CancellationToken.None);

        var closed = (await world.Host.RunStore.GetRunAsync(run.Id)).ShouldNotBeNull();

        closed.Status.ShouldBe(RunStatus.Failed);
        closed.EventCount.ShouldBe(42);
        closed.Usage.ShouldNotBeNull().TotalTokens.ShouldBe(300);
        closed.Cost.ShouldNotBeNull().InputCost.ShouldBe(0.5m);
        closed.NonTokenCost.ShouldNotBeNull().Amount.ShouldBe(1.75m);
    }

    [Fact]
    public async Task A_run_is_closed_when_its_last_request_expires_and_not_when_the_first_does()
    {
        var world = new World();
        var run = await world.WaitAsync();
        var first = (await world.Inbox.ListByRunAsync(run.Id)).Single();

        // A request a slower branch published later: it has three hours, the first has one.
        await world.Inbox.AddAsync(
        [
            first with { RequestId = "later-request", EventSequence = first.EventSequence + 1, ExpiresAt = first.RequestedAt + TimeSpan.FromHours(3) },
        ]);

        world.Clock.Advance(TimeSpan.FromHours(2));
        await world.Service.TickAsync(CancellationToken.None);

        (await world.Host.RunStore.GetRunAsync(run.Id))!.Status.ShouldBe(RunStatus.AwaitingInput);
        (await world.Inbox.ListByRunAsync(run.Id)).Select(static row => row.Status)
            .ShouldBe([WorkflowRequestStatus.Closed, WorkflowRequestStatus.Open], ignoreOrder: true);

        world.Clock.Advance(TimeSpan.FromHours(2));
        await world.Service.TickAsync(CancellationToken.None);

        (await world.Host.RunStore.GetRunAsync(run.Id))!.Status.ShouldBe(RunStatus.Failed);
    }

    [Fact]
    public async Task A_run_that_is_no_longer_waiting_is_left_as_it_is()
    {
        var world = new World();
        var run = await world.WaitAsync();
        var finishedAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        // Something else closed the run: its values are not the service's to replace.
        await world.Host.RunStore.CompleteRunAsync(new RunCompletion
        {
            RunId = run.Id,
            Status = RunStatus.Canceled,
            CompletedAt = finishedAt,
            EventCount = 41,
            TenantId = world.Host.TenantContext.TenantId,
        });

        world.Clock.Advance(Limit + TimeSpan.FromMinutes(1));
        await world.Service.TickAsync(CancellationToken.None);

        var after = (await world.Host.RunStore.GetRunAsync(run.Id)).ShouldNotBeNull();

        after.Status.ShouldBe(RunStatus.Canceled);
        after.EventCount.ShouldBe(41);
        after.CompletedAt.ShouldBe(finishedAt);
        after.Error.ShouldBeNull();
    }

    [Fact]
    public async Task A_run_whose_close_failed_is_closed_on_the_next_scan()
    {
        var world = new World(failures: 1);
        var run = await world.WaitAsync();

        world.Clock.Advance(Limit + TimeSpan.FromMinutes(1));

        // The row is closed first, and a closed row is never found again: the service itself
        // has to remember the run it could not close.
        await world.Service.TickAsync(CancellationToken.None);

        (await world.Host.RunStore.GetRunAsync(run.Id))!.Status.ShouldBe(RunStatus.AwaitingInput);
        (await world.Inbox.ListByRunAsync(run.Id)).ShouldHaveSingleItem().Status.ShouldBe(WorkflowRequestStatus.Closed);
        world.Logs.Entries.ShouldContain(static entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("the next scan tries again", StringComparison.Ordinal));

        await world.Service.TickAsync(CancellationToken.None);

        (await world.Host.RunStore.GetRunAsync(run.Id))!.Status.ShouldBe(RunStatus.Failed);
    }

    [Fact]
    public async Task A_run_left_waiting_by_a_process_that_stopped_after_closing_its_rows_is_closed_by_the_next_instance()
    {
        var world = new World();
        var run = await world.WaitAsync();

        world.Clock.Advance(Limit + TimeSpan.FromMinutes(1));

        // The first instance closed the row and stopped before it closed the run. Nobody remembers
        // the run, and a closed row is never found by the expiry again.
        await world.Inbox.ExpireAsync(world.Clock.GetUtcNow(), max: 100);

        (await world.Host.RunStore.GetRunAsync(run.Id))!.Status.ShouldBe(RunStatus.AwaitingInput);

        await world.NewService().TickAsync(CancellationToken.None);

        var closed = (await world.Host.RunStore.GetRunAsync(run.Id)).ShouldNotBeNull();

        closed.Status.ShouldBe(RunStatus.Failed);
        closed.Error.ShouldNotBeNull().Type.ShouldBe("WorkflowRequestExpired");
        closed.EventCount.ShouldBeGreaterThan(0);
        world.Logs.Entries.ShouldContain(static entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("their runs still wait", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_run_left_waiting_after_all_its_requests_were_answered_is_recovered_as_completed()
    {
        var world = new World();
        var run = await world.WaitAsync();
        var row = (await world.Inbox.ListByRunAsync(run.Id)).Single();

        await world.Inbox.MarkAnsweredAsync(row.SessionId, row.RequestId, Guid.NewGuid(), DateTimeOffset.UtcNow);
        world.Clock.Advance(Limit + TimeSpan.FromMinutes(1));
        await world.Inbox.ExpireAsync(world.Clock.GetUtcNow(), max: 100);

        await world.NewService().TickAsync(CancellationToken.None);

        var closed = (await world.Host.RunStore.GetRunAsync(run.Id)).ShouldNotBeNull();

        closed.Status.ShouldBe(RunStatus.Completed);
        closed.Error.ShouldBeNull();
    }

    [Fact]
    public async Task The_search_for_stranded_runs_is_made_once_and_not_on_every_scan()
    {
        var world = new World();

        await world.WaitAsync();
        await world.Service.TickAsync(CancellationToken.None);
        await world.Service.TickAsync(CancellationToken.None);
        await world.Service.TickAsync(CancellationToken.None);

        // Once when the instance took the lease; the answer was "nothing", so it is not asked again.
        world.Inbox.StrandedLookups.ShouldBe(1);
    }

    [Fact]
    public async Task A_search_that_fails_is_made_again_on_the_next_scan_and_does_not_stop_the_expiry()
    {
        var host = new WorkflowTestHost();
        using var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var inbox = new StrandedSearchFailsInbox(new InMemoryWorkflowRequestStore(host.TenantContext));
        var service = BuildService(
            inbox,
            host.RunStore,
            new AdjustableClock(DateTimeOffset.UtcNow),
            factory.CreateLogger<WorkflowRequestExpirationService>());

        await service.TickAsync(CancellationToken.None);
        await service.TickAsync(CancellationToken.None);

        inbox.Lookups.ShouldBe(2);
        inbox.Expiries.ShouldBe(2);
        logs.Entries.ShouldContain(static entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("left waiting after their requests closed failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_run_that_cannot_be_closed_is_given_up_after_ten_scans_and_logged_as_an_error()
    {
        var world = new World(failures: int.MaxValue);
        var run = await world.WaitAsync();

        world.Clock.Advance(Limit + TimeSpan.FromMinutes(1));

        for (var scan = 0; scan < 12; scan++)
        {
            await world.Service.TickAsync(CancellationToken.None);
        }

        (await world.Host.RunStore.GetRunAsync(run.Id))!.Status.ShouldBe(RunStatus.AwaitingInput);
        world.Logs.Entries.Count(static entry => entry.Level == LogLevel.Error && entry.Message.Contains("after 10 scans", StringComparison.Ordinal))
            .ShouldBe(1);

        // Ten tries are warnings, the eleventh scan no longer tries.
        world.Logs.Entries.Count(static entry => entry.Level == LogLevel.Warning && entry.Message.Contains("the next scan tries again", StringComparison.Ordinal))
            .ShouldBe(10);
    }

    [Fact]
    public async Task A_scan_that_finds_nothing_changes_nothing()
    {
        var world = new World();
        var run = await world.WaitAsync();

        await world.Service.TickAsync(CancellationToken.None);
        await world.Service.TickAsync(CancellationToken.None);

        (await world.Host.RunStore.GetRunAsync(run.Id))!.Status.ShouldBe(RunStatus.AwaitingInput);
    }

    [Fact]
    public async Task A_scan_never_throws_when_the_inbox_is_unreachable()
    {
        var host = new WorkflowTestHost();
        using var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var service = BuildService(
            new UnreachableInbox(),
            host.RunStore,
            new AdjustableClock(DateTimeOffset.UtcNow),
            factory.CreateLogger<WorkflowRequestExpirationService>());

        await service.TickAsync(CancellationToken.None);

        logs.Entries.ShouldContain(static entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("expiration scan failed", StringComparison.Ordinal));
    }

    private static readonly ServiceProvider OptionsProvider = new ServiceCollection()
        .AddOptions<TraconWorkflowOptions>().Services
        .AddOptions<SingletonExecutionOptions>().Services
        .BuildServiceProvider();

    private static WorkflowRequestExpirationService BuildService(
        IWorkflowRequestStore inbox,
        IRunStore runs,
        TimeProvider clock,
        ILogger<WorkflowRequestExpirationService> logger)
        => new(
            inbox,
            runs,
            new InMemorySingletonLeaseStore(clock),
            OptionsProvider.GetRequiredService<IOptionsMonitor<TraconWorkflowOptions>>(),
            OptionsProvider.GetRequiredService<IOptionsMonitor<SingletonExecutionOptions>>(),
            new SchemaReadyGate([]),
            clock,
            logger,
            new DefaultRunErrorClassifier());

    /// <summary>The engine, the stores, a clock and the service over them.</summary>
    private sealed class World : IDisposable
    {
        private readonly string _workflow;
        private readonly LoggerFactory _factory;

        public World(CodeWorkflowRegistration? registration = null, string workflow = "approval-flow", int failures = 0)
        {
            _workflow = workflow;
            Host = new WorkflowTestHost();
            Inbox = new StrandingInbox(new InMemoryWorkflowRequestStore(Host.TenantContext), Host.RunStore);
            Clock = new AdjustableClock(DateTimeOffset.UtcNow);
            Logs = new CapturingLoggerProvider();
            _factory = new LoggerFactory([Logs]);

            Runner = Host.CreateRunnerWithRequestStore(
                Inbox,
                logger: null,
                registration ?? ApprovalWorkflow.Registration() with { RequestTimeout = Limit });

            _runs = failures > 0 ? new FlakyCompletionRunStore(Host.RunStore, failures) : Host.RunStore;
            Service = NewService();
        }

        private readonly IRunStore _runs;

        public WorkflowTestHost Host { get; }

        public StrandingInbox Inbox { get; }

        public AdjustableClock Clock { get; }

        public CapturingLoggerProvider Logs { get; }

        public WorkflowRunner Runner { get; }

        public WorkflowRequestExpirationService Service { get; }

        /// <summary>A service of its own over the same stores: the instance that takes over after another stopped.</summary>
        public WorkflowRequestExpirationService NewService()
            => BuildService(Inbox, _runs, Clock, _factory.CreateLogger<WorkflowRequestExpirationService>());

        public async Task<RunRecord> WaitAsync()
        {
            await foreach (var _ in Runner.RunStreamingAsync(new WorkflowRunRequest { WorkflowName = _workflow, Message = "publish" }))
            {
            }

            var run = (await Host.RunStore.QueryRunsAsync(new RunQuery { Kind = RunKind.Workflow, Take = 10 })).Single();

            run.Status.ShouldBe(RunStatus.AwaitingInput);

            // The clock starts at the moment the run published its request, so "the limit passed"
            // and "the limit did not pass yet" are exact.
            var published = (await Inbox.ListByRunAsync(run.Id)).Min(static row => row.RequestedAt);

            Clock.Advance(published - Clock.GetUtcNow());

            return run;
        }

        public void Dispose()
        {
            _factory.Dispose();
            Logs.Dispose();
        }
    }

    /// <summary>
    /// The in-memory inbox with the stranded-run listing the SQL stores answer with a join: it
    /// remembers the rows <c>ExpireAsync</c> closed and lists those whose run still waits and has
    /// no open row. The join itself is proven by <c>WorkflowRequestStoreContract</c>; this lets
    /// the service be driven through the situation a stopped process leaves.
    /// </summary>
    private sealed class StrandingInbox(InMemoryWorkflowRequestStore inner, IRunStore runs) : IWorkflowRequestStore
    {
        private readonly List<WorkflowRequestRecord> _closed = [];

        public int StrandedLookups { get; private set; }

        public ValueTask AddAsync(IReadOnlyList<WorkflowRequestRecord> records, CancellationToken cancellationToken = default)
            => inner.AddAsync(records, cancellationToken);

        public ValueTask<IReadOnlyList<WorkflowRequestRecord>> ListAsync(WorkflowRequestQuery query, CancellationToken cancellationToken = default)
            => inner.ListAsync(query, cancellationToken);

        public ValueTask<IReadOnlyList<WorkflowRequestRecord>> ListByRunAsync(Guid runId, CancellationToken cancellationToken = default)
            => inner.ListByRunAsync(runId, cancellationToken);

        public ValueTask<int> MarkAnsweredAsync(string sessionId, string requestId, Guid responseRunId, DateTimeOffset answeredAt, CancellationToken cancellationToken = default)
            => inner.MarkAnsweredAsync(sessionId, requestId, responseRunId, answeredAt, cancellationToken);

        public async ValueTask<IReadOnlyList<WorkflowRequestRecord>> ExpireAsync(DateTimeOffset now, int max, CancellationToken cancellationToken = default)
        {
            var closed = await inner.ExpireAsync(now, max, cancellationToken);

            _closed.AddRange(closed);

            return closed;
        }

        public async ValueTask<IReadOnlyList<WorkflowRequestRecord>> ListStrandedAsync(int max, CancellationToken cancellationToken = default)
        {
            StrandedLookups++;

            var stranded = new List<WorkflowRequestRecord>();

            foreach (var row in _closed)
            {
                var run = await runs.GetRunAsync(row.RunId, cancellationToken);
                var siblings = await inner.ListByRunAsync(row.RunId, cancellationToken);

                if (run is { Status: RunStatus.AwaitingInput } &&
                    siblings.All(static sibling => sibling.Status == WorkflowRequestStatus.Closed))
                {
                    stranded.Add(row);
                }
            }

            return stranded.Take(max).ToList();
        }
    }

    private sealed class StrandedSearchFailsInbox(InMemoryWorkflowRequestStore inner) : IWorkflowRequestStore
    {
        public int Lookups { get; private set; }

        public int Expiries { get; private set; }

        public ValueTask AddAsync(IReadOnlyList<WorkflowRequestRecord> records, CancellationToken cancellationToken = default)
            => inner.AddAsync(records, cancellationToken);

        public ValueTask<IReadOnlyList<WorkflowRequestRecord>> ListAsync(WorkflowRequestQuery query, CancellationToken cancellationToken = default)
            => inner.ListAsync(query, cancellationToken);

        public ValueTask<IReadOnlyList<WorkflowRequestRecord>> ListByRunAsync(Guid runId, CancellationToken cancellationToken = default)
            => inner.ListByRunAsync(runId, cancellationToken);

        public ValueTask<int> MarkAnsweredAsync(string sessionId, string requestId, Guid responseRunId, DateTimeOffset answeredAt, CancellationToken cancellationToken = default)
            => inner.MarkAnsweredAsync(sessionId, requestId, responseRunId, answeredAt, cancellationToken);

        public ValueTask<IReadOnlyList<WorkflowRequestRecord>> ExpireAsync(DateTimeOffset now, int max, CancellationToken cancellationToken = default)
        {
            Expiries++;

            return inner.ExpireAsync(now, max, cancellationToken);
        }

        public ValueTask<IReadOnlyList<WorkflowRequestRecord>> ListStrandedAsync(int max, CancellationToken cancellationToken = default)
        {
            Lookups++;

            throw new InvalidOperationException("the search is down");
        }
    }

    private sealed class UnreachableInbox : IWorkflowRequestStore
    {
        public ValueTask AddAsync(IReadOnlyList<WorkflowRequestRecord> records, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the inbox is unreachable");

        public ValueTask<IReadOnlyList<WorkflowRequestRecord>> ListAsync(WorkflowRequestQuery query, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the inbox is unreachable");

        public ValueTask<IReadOnlyList<WorkflowRequestRecord>> ListByRunAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the inbox is unreachable");

        public ValueTask<int> MarkAnsweredAsync(string sessionId, string requestId, Guid responseRunId, DateTimeOffset answeredAt, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the inbox is unreachable");

        public ValueTask<IReadOnlyList<WorkflowRequestRecord>> ExpireAsync(DateTimeOffset now, int max, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the inbox is unreachable");

        public ValueTask<IReadOnlyList<WorkflowRequestRecord>> ListStrandedAsync(int max, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the inbox is unreachable");
    }
}
