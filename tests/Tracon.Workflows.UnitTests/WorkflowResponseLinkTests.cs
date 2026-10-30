using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// What the engine itself writes for an answer: the run that answers names its
/// source in a <see cref="RunEventType.WorkflowResponseApplied"/> event, and marks the answer
/// delivered once the workflow received it - best-effort, never at the run's expense.
/// </summary>
/// <remarks>
/// The engine is the only place both facts are known: the endpoint has already returned when
/// the answer reaches the graph. The HTTP-level proof of the same contract is in
/// <c>WorkflowResponseRecordTests</c> and <c>WorkflowSingleResponseTests</c>.
/// </remarks>
public sealed class WorkflowResponseLinkTests
{
    [Fact]
    public async Task An_answering_run_names_its_source_right_after_it_starts_and_marks_the_answer_delivered()
    {
        var host = new WorkflowTestHost();
        var store = new InMemoryWorkflowResponseStore(host.TenantContext);
        var runner = host.CreateRunnerWithResponseStore(store, logger: null, ApprovalWorkflow.Registration());
        var (sourceId, requestId) = await WaitAsync(runner, host);
        var answerRunId = Guid.NewGuid();

        await store.AddAsync(Record(host, sourceId, requestId, answerRunId));

        var events = await AnswerAsync(runner, sourceId, requestId, answerRunId);

        events[0].Type.ShouldBe(RunEventType.RunStarted);
        events[1].Type.ShouldBe(RunEventType.WorkflowResponseApplied);
        events[1].Sequence.ShouldBe(1);
        events[1].Text.ShouldBe(requestId);

        var payload = JsonDocument.Parse(events[1].Payload.ShouldNotBeNull()).RootElement;

        payload.GetProperty("sourceRunId").GetGuid().ShouldBe(sourceId);
        payload.GetProperty("requestId").GetString().ShouldBe(requestId);

        (await store.GetAsync(answerRunId)).ShouldNotBeNull().Status.ShouldBe(WorkflowResponseStatus.Delivered);
    }

    [Fact]
    public async Task A_run_and_a_resume_answer_nothing_and_name_no_source()
    {
        var host = new WorkflowTestHost();
        var runner = host.CreateRunnerWithResponseStore(
            new InMemoryWorkflowResponseStore(host.TenantContext), logger: null, ApprovalWorkflow.Registration());
        var (sourceId, _) = await WaitAsync(runner, host);
        var resumed = new List<RunEvent>();

        await foreach (var runEvent in runner.ResumeStreamingAsync(new WorkflowResumeRequest { RunId = sourceId }))
        {
            resumed.Add(runEvent);
        }

        resumed.ShouldNotContain(static runEvent => runEvent.Type == RunEventType.WorkflowResponseApplied);

        var started = await host.RunStore.ReadEventsAsync(sourceId, 0).ToListAsync();

        started.ShouldNotContain(static runEvent => runEvent.Type == RunEventType.WorkflowResponseApplied);
    }

    [Fact]
    public async Task A_delivery_that_cannot_be_recorded_is_logged_and_the_run_completes()
    {
        var host = new WorkflowTestHost();
        using var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var store = new UndeliverableStore(new InMemoryWorkflowResponseStore(host.TenantContext));
        var runner = host.CreateRunnerWithResponseStore(store, factory.CreateLogger<WorkflowRunner>(), ApprovalWorkflow.Registration());
        var (sourceId, requestId) = await WaitAsync(runner, host);
        var answerRunId = Guid.NewGuid();

        await store.AddAsync(Record(host, sourceId, requestId, answerRunId));

        var events = await AnswerAsync(runner, sourceId, requestId, answerRunId);

        events[^1].Type.ShouldBe(RunEventType.RunCompleted);
        (await store.GetAsync(answerRunId))!.Status.ShouldBe(WorkflowResponseStatus.Accepted);
        logs.Entries.ShouldContain(static entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("could not be recorded", StringComparison.Ordinal));
    }

    [Fact]
    public void The_registration_overload_carries_the_mode_and_refuses_an_unknown_one()
    {
        var services = new ServiceCollection();
        var builder = new TestBuilder(services);

        builder.AddWorkflow("approval", static _ => ApprovalWorkflow.Build(), static options =>
        {
            options.Description = "Asks once.";
            options.ResponseMode = WorkflowResponseMode.SingleAnswer;
        });

        var registration = services
            .Select(static descriptor => descriptor.ImplementationInstance)
            .OfType<CodeWorkflowRegistration>()
            .ShouldHaveSingleItem();

        registration.Description.ShouldBe("Asks once.");
        registration.ResponseMode.ShouldBe(WorkflowResponseMode.SingleAnswer);

        Should.Throw<ArgumentException>(() => builder.AddWorkflow(
            "broken", static _ => ApprovalWorkflow.Build(), static options => options.ResponseMode = (WorkflowResponseMode)7));
    }

    [Fact]
    public async Task The_catalog_describes_a_code_workflow_with_its_mode()
    {
        var host = new WorkflowTestHost();
        var runner = host.CreateRunner(
            configure: null,
            services: null,
            new CodeWorkflowRegistration("asks-once", null, static _ => ApprovalWorkflow.Build(), WorkflowResponseMode.SingleAnswer),
            ApprovalWorkflow.Registration());

        (await runner.GetAsync("asks-once")).ShouldNotBeNull().ResponseMode.ShouldBe(WorkflowResponseMode.SingleAnswer);
        (await runner.ListAsync()).Single(static workflow => string.Equals(workflow.Name, "approval-flow", StringComparison.Ordinal))
            .ResponseMode.ShouldBe(WorkflowResponseMode.Branch);
    }

    private static async Task<(Guid RunId, string RequestId)> WaitAsync(WorkflowRunner runner, WorkflowTestHost host)
    {
        await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest { WorkflowName = "approval-flow", Message = "publish" }))
        {
        }

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();

        return (run.Id, (await runner.ListPendingRequestsAsync(run.Id)).Single().RequestId);
    }

    private static async Task<List<RunEvent>> AnswerAsync(WorkflowRunner runner, Guid sourceId, string requestId, Guid answerRunId)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in runner.RespondStreamingAsync(new WorkflowRespondRequest
        {
            RunId = sourceId,
            RequestId = requestId,
            Approved = true,
            NewRunId = answerRunId,
        }))
        {
            events.Add(runEvent);
        }

        return events;
    }

    private static WorkflowResponseRecord Record(WorkflowTestHost host, Guid sourceId, string requestId, Guid answerRunId)
        => new()
        {
            ResponseRunId = answerRunId,
            SourceRunId = sourceId,
            SessionId = "session",
            RequestId = requestId,
            AnswerHash = new string('a', 64),
            ClaimScope = null,
            Status = WorkflowResponseStatus.Accepted,
            RespondedBy = null,
            RespondedAt = DateTimeOffset.UtcNow,
            TenantId = host.TenantContext.TenantId,
        };

    private sealed class TestBuilder(IServiceCollection services) : ITraconBuilder
    {
        public IServiceCollection Services { get; } = services;
    }

    private sealed class UndeliverableStore(IWorkflowResponseStore inner) : IWorkflowResponseStore
    {
        public ValueTask<WorkflowResponseClaim> AddAsync(WorkflowResponseRecord record, CancellationToken cancellationToken = default)
            => inner.AddAsync(record, cancellationToken);

        public ValueTask<WorkflowResponseRecord?> FindClaimAsync(string claimScope, string requestId, CancellationToken cancellationToken = default)
            => inner.FindClaimAsync(claimScope, requestId, cancellationToken);

        public ValueTask<bool> ReplaceAsync(Guid abandonedResponseRunId, WorkflowResponseRecord replacement, CancellationToken cancellationToken = default)
            => inner.ReplaceAsync(abandonedResponseRunId, replacement, cancellationToken);

        public ValueTask MarkDeliveredAsync(Guid responseRunId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the answer store is unreachable");

        public ValueTask<IReadOnlyList<WorkflowResponseRecord>> ListBySourceRunAsync(Guid sourceRunId, CancellationToken cancellationToken = default)
            => inner.ListBySourceRunAsync(sourceRunId, cancellationToken);

        public ValueTask<WorkflowResponseRecord?> GetAsync(Guid responseRunId, CancellationToken cancellationToken = default)
            => inner.GetAsync(responseRunId, cancellationToken);
    }
}
