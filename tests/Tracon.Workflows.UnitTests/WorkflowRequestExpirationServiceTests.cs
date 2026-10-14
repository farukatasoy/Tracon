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
            Inbox = new InMemoryWorkflowRequestStore(Host.TenantContext);
            Clock = new AdjustableClock(DateTimeOffset.UtcNow);
            Logs = new CapturingLoggerProvider();
            _factory = new LoggerFactory([Logs]);

            Runner = Host.CreateRunnerWithRequestStore(
                Inbox,
                logger: null,
                registration ?? ApprovalWorkflow.Registration() with { RequestTimeout = Limit });

            Service = BuildService(
                Inbox,
                failures > 0 ? new FlakyCompletionRunStore(Host.RunStore, failures) : Host.RunStore,
                Clock,
                _factory.CreateLogger<WorkflowRequestExpirationService>());
        }

        public WorkflowTestHost Host { get; }

        public InMemoryWorkflowRequestStore Inbox { get; }

        public AdjustableClock Clock { get; }

        public CapturingLoggerProvider Logs { get; }

        public WorkflowRunner Runner { get; }

        public WorkflowRequestExpirationService Service { get; }

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
    }
}
