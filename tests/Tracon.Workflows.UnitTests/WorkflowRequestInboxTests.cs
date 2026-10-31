using Microsoft.Extensions.Logging;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// What the engine writes into the inbox of workflow requests when a run starts waiting
/// And what it lists from it.
/// </summary>
/// <remarks>
/// <para>
/// The engine is the one place that knows both facts a row needs: the request the run published
/// (its event) and the limit of the definition the run was compiled from. The HTTP proof of the
/// same contract is in <c>WorkflowRequestInboxHttpTests</c>; the expiry is in
/// <c>WorkflowRequestExpirationServiceTests</c>.
/// </para>
/// <para>
/// A row is a PROJECTION: the prompt and the form stay in the run's event, which the row names by
/// its sequence. Two cases pin that, because a copy of the prompt would leave the inbox outside the
/// content protection the event stream has.
/// </para>
/// </remarks>
public sealed class WorkflowRequestInboxTests
{
    [Fact]
    public async Task A_run_that_waits_writes_a_row_for_its_request_with_the_time_it_expires()
    {
        var host = new WorkflowTestHost();
        var inbox = new InMemoryWorkflowRequestStore(host.TenantContext);
        var runner = host.CreateRunnerWithRequestStore(
            inbox,
            logger: null,
            ApprovalWorkflow.Registration() with { RequestTimeout = TimeSpan.FromHours(2) });

        var run = await WaitAsync(runner, host, ApprovalWorkflow.Registration().Name);

        var row = (await inbox.ListByRunAsync(run.Id)).ShouldHaveSingleItem();

        row.RunId.ShouldBe(run.Id);
        row.WorkflowName.ShouldBe("approval-flow");
        row.SessionId.ShouldBe(run.SessionId);
        row.PortId.ShouldBe(ApprovalWorkflow.PortId);
        row.Status.ShouldBe(WorkflowRequestStatus.Open);
        row.AnsweredAt.ShouldBeNull();
        row.ExpiresAt.ShouldBe(row.RequestedAt + TimeSpan.FromHours(2));
        row.TenantId.ShouldBe(host.TenantContext.TenantId);

        // The row names the event that holds the request: the sequence is the request's own.
        var requestEvent = (await host.RunStore.ReadEventsAsync(run.Id, 0).ToListAsync())
            .Single(runEvent => runEvent.Sequence == row.EventSequence);

        requestEvent.Type.ShouldBe(RunEventType.WorkflowRequest);
        requestEvent.Text.ShouldBe(row.RequestId);
        requestEvent.Timestamp.ShouldBe(row.RequestedAt);
    }

    [Fact]
    public async Task A_workflow_without_a_limit_writes_a_row_that_never_expires()
    {
        var host = new WorkflowTestHost();
        var inbox = new InMemoryWorkflowRequestStore(host.TenantContext);
        var runner = host.CreateRunnerWithRequestStore(inbox, logger: null, ApprovalWorkflow.Registration());

        var run = await WaitAsync(runner, host, "approval-flow");

        (await inbox.ListByRunAsync(run.Id)).ShouldHaveSingleItem().ExpiresAt.ShouldBeNull();
        (await inbox.ExpireAsync(DateTimeOffset.UtcNow.AddYears(10), max: 100)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_that_finishes_without_waiting_writes_no_row()
    {
        var host = new WorkflowTestHost("writer");
        var inbox = new InMemoryWorkflowRequestStore(host.TenantContext);
        var runner = host.CreateRunnerWithRequestStore(inbox, logger: null);

        await host.SaveAsync(new WorkflowDefinition { Name = "chain", Kind = WorkflowKind.Sequential, AgentNames = ["writer"] });

        await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest { WorkflowName = "chain", Message = "hello" }))
        {
        }

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { Kind = RunKind.Workflow, Take = 10 })).Single();

        run.Status.ShouldBe(RunStatus.Completed);
        (await inbox.ListByRunAsync(run.Id)).ShouldBeEmpty();
        (await inbox.ListAsync(new WorkflowRequestQuery { State = WorkflowRequestState.All })).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_stored_definition_gives_its_limit_to_the_rows_of_its_run()
    {
        // A stored definition asks a human through a manager agent's plan approval, which is the
        // only request a stored graph publishes.
        var host = new WorkflowTestHost("manager", "worker");
        var inbox = new InMemoryWorkflowRequestStore(host.TenantContext);
        var runner = host.CreateRunnerWithRequestStore(inbox, logger: null);

        await SavePlannedAsync(host, TimeSpan.FromDays(3));

        var run = await WaitAsync(runner, host, "planned");
        var row = (await inbox.ListByRunAsync(run.Id)).ShouldHaveSingleItem();

        row.ExpiresAt.ShouldBe(row.RequestedAt + TimeSpan.FromDays(3));
        row.WorkflowName.ShouldBe("planned");
    }

    [Fact]
    public async Task An_edit_made_while_the_run_waits_does_not_move_the_time_it_expires()
    {
        var host = new WorkflowTestHost("manager", "worker");
        var inbox = new InMemoryWorkflowRequestStore(host.TenantContext);
        var runner = host.CreateRunnerWithRequestStore(inbox, logger: null);

        await SavePlannedAsync(host, TimeSpan.FromDays(3));

        var run = await WaitAsync(runner, host, "planned");

        // Saved again while the run waits: the new version allows a day.
        await SavePlannedAsync(host, TimeSpan.FromDays(1));

        var row = (await inbox.ListByRunAsync(run.Id)).ShouldHaveSingleItem();

        row.ExpiresAt.ShouldBe(row.RequestedAt + TimeSpan.FromDays(3));
    }

    [Fact]
    public async Task One_request_published_by_two_runs_is_listed_once_under_the_newest()
    {
        var host = new WorkflowTestHost();
        var inbox = new InMemoryWorkflowRequestStore(host.TenantContext);
        var runner = host.CreateRunnerWithRequestStore(inbox, logger: null, TwoQuestionWorkflow.Registration(TimeSpan.FromHours(1)));

        var first = await WaitAsync(runner, host, TwoQuestionWorkflow.Name);
        var requests = await runner.ListPendingRequestsAsync(first.Id);

        requests.Count.ShouldBe(2);

        var answered = requests.Single(static request => string.Equals(request.PortId, TwoQuestionWorkflow.FirstPort, StringComparison.Ordinal));
        var open = requests.Single(static request => string.Equals(request.PortId, TwoQuestionWorkflow.SecondPort, StringComparison.Ordinal));
        var continuation = Guid.NewGuid();

        await foreach (var _ in runner.RespondStreamingAsync(new WorkflowRespondRequest
        {
            RunId = first.Id,
            RequestId = answered.RequestId,
            Approved = true,
            NewRunId = continuation,
        }))
        {
        }

        // The continuation republished the request that is still open - with the same id - and
        // waits on it; the run it continued stays AwaitingInput and still lists both.
        var next = (await host.RunStore.GetRunAsync(continuation)).ShouldNotBeNull();

        next.Status.ShouldBe(RunStatus.AwaitingInput);
        (await runner.ListPendingRequestsAsync(continuation)).ShouldHaveSingleItem().RequestId.ShouldBe(open.RequestId);

        // The endpoint marks the answer; the engine does not.
        await inbox.MarkAnsweredAsync(first.SessionId!, answered.RequestId, continuation, DateTimeOffset.UtcNow);

        var all = await runner.ListRequestsAsync(new WorkflowRequestQuery { State = WorkflowRequestState.All });

        all.Count.ShouldBe(2);
        all.Single(request => string.Equals(request.RequestId, open.RequestId, StringComparison.Ordinal)).RunId.ShouldBe(continuation);
        all.Single(request => string.Equals(request.RequestId, answered.RequestId, StringComparison.Ordinal)).RunId.ShouldBe(first.Id);

        var pending = await runner.ListRequestsAsync(new WorkflowRequestQuery { State = WorkflowRequestState.Pending });

        pending.ShouldHaveSingleItem().RequestId.ShouldBe(open.RequestId);
        pending[0].Answered.ShouldBeFalse();

        var done = (await runner.ListRequestsAsync(new WorkflowRequestQuery { State = WorkflowRequestState.Answered })).ShouldHaveSingleItem();

        done.RequestId.ShouldBe(answered.RequestId);
        done.Answered.ShouldBeTrue();
        done.LastResponseRunId.ShouldBe(continuation);
    }

    [Fact]
    public async Task The_tenant_wide_list_reads_the_prompt_and_the_form_from_the_run_event()
    {
        var host = new WorkflowTestHost();
        var inbox = new InMemoryWorkflowRequestStore(host.TenantContext);
        var runner = host.CreateRunnerWithRequestStore(inbox, logger: null, ApprovalWorkflow.Registration() with { RequestTimeout = TimeSpan.FromHours(2) });

        var run = await WaitAsync(runner, host, "approval-flow");
        var listed = (await runner.ListRequestsAsync(new WorkflowRequestQuery { State = WorkflowRequestState.All })).ShouldHaveSingleItem();
        var perRun = (await runner.ListPendingRequestsAsync(run.Id)).ShouldHaveSingleItem();

        listed.RunId.ShouldBe(run.Id);
        listed.WorkflowName.ShouldBe("approval-flow");
        listed.SessionId.ShouldBe(run.SessionId);
        listed.Prompt.ShouldBe("Approval requested: publish");
        listed.Form.ShouldBe(WorkflowRequestForm.Boolean);
        listed.PortId.ShouldBe(ApprovalWorkflow.PortId);
        listed.ExpiresAt.ShouldNotBeNull();
        listed.Answered.ShouldBeFalse();
        listed.LastResponseRunId.ShouldBeNull();

        // The per-run list carries the same facts.
        perRun.ShouldBe(listed);
    }

    [Fact]
    public async Task A_row_whose_event_is_gone_is_listed_with_the_facts_of_the_row()
    {
        var host = new WorkflowTestHost();
        var inbox = new InMemoryWorkflowRequestStore(host.TenantContext);
        var runner = host.CreateRunnerWithRequestStore(inbox, logger: null, ApprovalWorkflow.Registration());
        var run = await WaitAsync(runner, host, "approval-flow");

        await inbox.AddAsync(
        [
            new WorkflowRequestRecord
            {
                RunId = run.Id,
                RequestId = "lost-request",
                SessionId = run.SessionId!,
                WorkflowName = "approval-flow",
                PortId = "lost-port",
                EventSequence = 9_999,
                RequestedAt = DateTimeOffset.UtcNow.AddMinutes(1),
                Status = WorkflowRequestStatus.Open,
                TenantId = host.TenantContext.TenantId,
            },
        ]);

        var lost = (await runner.ListRequestsAsync(new WorkflowRequestQuery { State = WorkflowRequestState.All }))
            .Single(static request => string.Equals(request.RequestId, "lost-request", StringComparison.Ordinal));

        // Answerable by its id from the run, and the card of the run shows the rest.
        lost.RunId.ShouldBe(run.Id);
        lost.PortId.ShouldBe("lost-port");
        lost.Prompt.ShouldBeNull();
        lost.Form.ShouldBe(WorkflowRequestForm.Json);
    }

    [Fact]
    public async Task A_row_that_cannot_be_written_is_logged_and_the_run_still_waits_and_can_be_answered()
    {
        var host = new WorkflowTestHost();
        using var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var inbox = new UnwritableInbox(new InMemoryWorkflowRequestStore(host.TenantContext));
        var runner = host.CreateRunnerWithRequestStore(inbox, factory.CreateLogger<WorkflowRunner>(), ApprovalWorkflow.Registration());

        var run = await WaitAsync(runner, host, "approval-flow");

        run.Status.ShouldBe(RunStatus.AwaitingInput);
        logs.Entries.ShouldContain(static entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("inbox rows", StringComparison.Ordinal));

        // The request is still listed from the run and still answerable; it is only missing from the tenant-wide list.
        var request = (await runner.ListPendingRequestsAsync(run.Id)).ShouldHaveSingleItem();
        var events = new List<RunEvent>();

        await foreach (var runEvent in runner.RespondStreamingAsync(new WorkflowRespondRequest
        {
            RunId = run.Id,
            RequestId = request.RequestId,
            Approved = true,
        }))
        {
            events.Add(runEvent);
        }

        events[^1].Type.ShouldBe(RunEventType.RunCompleted);
        (await runner.ListRequestsAsync(new WorkflowRequestQuery { State = WorkflowRequestState.All })).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unreadable_inbox_does_not_stop_the_requests_of_a_run_from_being_listed()
    {
        var host = new WorkflowTestHost();
        using var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var runner = host.CreateRunnerWithRequestStore(
            new UnreadableInbox(new InMemoryWorkflowRequestStore(host.TenantContext)),
            factory.CreateLogger<WorkflowRunner>(),
            ApprovalWorkflow.Registration() with { RequestTimeout = TimeSpan.FromHours(1) });

        var run = await WaitAsync(runner, host, "approval-flow");

        // Answering a request reads this list; it must not depend on the projection.
        var request = (await runner.ListPendingRequestsAsync(run.Id)).ShouldHaveSingleItem();

        request.ExpiresAt.ShouldBeNull();
        request.Answered.ShouldBeFalse();
        request.WorkflowName.ShouldBe("approval-flow");
        logs.Entries.ShouldContain(static entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("could not be read", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_runner_without_an_inbox_lists_nothing_across_runs_and_the_requests_of_a_run_as_before()
    {
        var host = new WorkflowTestHost();
        var runner = host.CreateRunner(configure: null, services: null, ApprovalWorkflow.Registration());

        var run = await WaitAsync(runner, host, "approval-flow");

        (await runner.ListRequestsAsync(new WorkflowRequestQuery { State = WorkflowRequestState.All })).ShouldBeEmpty();

        var request = (await runner.ListPendingRequestsAsync(run.Id)).ShouldHaveSingleItem();

        request.ExpiresAt.ShouldBeNull();
        request.Answered.ShouldBeFalse();
    }

    private static async Task SavePlannedAsync(WorkflowTestHost host, TimeSpan requestTimeout)
        => await host.SaveAsync(new WorkflowDefinition
        {
            Name = "planned",
            Kind = WorkflowKind.Magentic,
            ManagerAgentName = "manager",
            AgentNames = ["worker"],
            MaxIterations = 2,
            RequirePlanApproval = true,
            RequestTimeout = requestTimeout,
        });

    /// <summary>Runs the named workflow until it waits, and returns its run.</summary>
    private static async Task<RunRecord> WaitAsync(WorkflowRunner runner, WorkflowTestHost host, string workflow)
    {
        await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest { WorkflowName = workflow, Message = "publish" }))
        {
        }

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { Kind = RunKind.Workflow, Take = 10 })).Single();

        run.Status.ShouldBe(RunStatus.AwaitingInput);

        return run;
    }

    private sealed class UnwritableInbox(IWorkflowRequestStore inner) : IWorkflowRequestStore
    {
        public ValueTask AddAsync(IReadOnlyList<WorkflowRequestRecord> records, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the inbox is unreachable");

        public ValueTask<IReadOnlyList<WorkflowRequestRecord>> ListAsync(WorkflowRequestQuery query, CancellationToken cancellationToken = default)
            => inner.ListAsync(query, cancellationToken);

        public ValueTask<IReadOnlyList<WorkflowRequestRecord>> ListByRunAsync(Guid runId, CancellationToken cancellationToken = default)
            => inner.ListByRunAsync(runId, cancellationToken);

        public ValueTask<int> MarkAnsweredAsync(string sessionId, string requestId, Guid responseRunId, DateTimeOffset answeredAt, CancellationToken cancellationToken = default)
            => inner.MarkAnsweredAsync(sessionId, requestId, responseRunId, answeredAt, cancellationToken);

        public ValueTask<IReadOnlyList<WorkflowRequestRecord>> ExpireAsync(DateTimeOffset now, int max, CancellationToken cancellationToken = default)
            => inner.ExpireAsync(now, max, cancellationToken);

        public ValueTask<IReadOnlyList<WorkflowRequestRecord>> ListStrandedAsync(int max, CancellationToken cancellationToken = default)
            => inner.ListStrandedAsync(max, cancellationToken);
    }

    private sealed class UnreadableInbox(IWorkflowRequestStore inner) : IWorkflowRequestStore
    {
        public ValueTask AddAsync(IReadOnlyList<WorkflowRequestRecord> records, CancellationToken cancellationToken = default)
            => inner.AddAsync(records, cancellationToken);

        public ValueTask<IReadOnlyList<WorkflowRequestRecord>> ListAsync(WorkflowRequestQuery query, CancellationToken cancellationToken = default)
            => inner.ListAsync(query, cancellationToken);

        public ValueTask<IReadOnlyList<WorkflowRequestRecord>> ListByRunAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the inbox is unreachable");

        public ValueTask<int> MarkAnsweredAsync(string sessionId, string requestId, Guid responseRunId, DateTimeOffset answeredAt, CancellationToken cancellationToken = default)
            => inner.MarkAnsweredAsync(sessionId, requestId, responseRunId, answeredAt, cancellationToken);

        public ValueTask<IReadOnlyList<WorkflowRequestRecord>> ExpireAsync(DateTimeOffset now, int max, CancellationToken cancellationToken = default)
            => inner.ExpireAsync(now, max, cancellationToken);

        public ValueTask<IReadOnlyList<WorkflowRequestRecord>> ListStrandedAsync(int max, CancellationToken cancellationToken = default)
            => inner.ListStrandedAsync(max, cancellationToken);
    }
}
