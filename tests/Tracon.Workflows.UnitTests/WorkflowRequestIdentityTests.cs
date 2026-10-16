using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// Pins how Microsoft Agent Framework names a pending request inside one execution
/// session. The single-answer claim of a workflow is keyed by
/// <c>(tenant, session, request id)</c>, and that key is only sound while both rules below hold.
/// </summary>
/// <remarks>
/// <para>
/// Measured on MAF 1.20.0 (Phase 208, measurement 1; the tests pass on 1.25.0, re-run
/// 2026-10-16): a request port issues
/// <c>Guid.NewGuid().ToString("N")</c> for every request it publishes, and a run resumed
/// from a checkpoint republishes the pending request under the id it already had. A MAF
/// upgrade that changes either rule breaks this class first.
/// </para>
/// </remarks>
public sealed class WorkflowRequestIdentityTests
{
    private const string Name = "ask-twice";

    [Fact]
    public async Task A_second_question_in_the_same_session_gets_a_new_request_id()
    {
        var host = new WorkflowTestHost();
        var runner = host.CreateRunner(configure: null, services: null, Registration());

        await DrainAsync(runner.RunStreamingAsync(new WorkflowRunRequest { WorkflowName = Name, Message = "draft" }));

        var source = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();
        var first = (await runner.ListPendingRequestsAsync(source.Id)).ShouldHaveSingleItem().RequestId;

        // "No" sends the graph back to the same port: the session asks again.
        var asked = await AnswerAsync(runner, source.Id, first);
        var askedRun = await host.RunStore.GetRunAsync(asked);

        askedRun.ShouldNotBeNull().Status.ShouldBe(RunStatus.AwaitingInput);
        askedRun.SessionId.ShouldBe(source.SessionId);

        var second = (await runner.ListPendingRequestsAsync(asked)).ShouldHaveSingleItem().RequestId;

        string.Equals(second, first, StringComparison.Ordinal).ShouldBeFalse();

        // A branch (K-130) that asks again in the same session is a third, distinct request.
        var branch = await AnswerAsync(runner, source.Id, first);
        var third = (await runner.ListPendingRequestsAsync(branch)).ShouldHaveSingleItem().RequestId;

        string.Equals(third, first, StringComparison.Ordinal).ShouldBeFalse();
        string.Equals(third, second, StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public async Task A_resumed_run_republishes_the_pending_request_under_the_same_id()
    {
        var host = new WorkflowTestHost();
        var runner = host.CreateRunner(configure: null, services: null, Registration());

        await DrainAsync(runner.RunStreamingAsync(new WorkflowRunRequest { WorkflowName = Name, Message = "draft" }));

        var source = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();
        var pending = (await runner.ListPendingRequestsAsync(source.Id)).ShouldHaveSingleItem().RequestId;

        var resumedId = Guid.NewGuid();

        await DrainAsync(runner.ResumeStreamingAsync(new WorkflowResumeRequest { RunId = source.Id, NewRunId = resumedId }));

        (await runner.ListPendingRequestsAsync(resumedId)).ShouldHaveSingleItem().RequestId.ShouldBe(pending);
    }

    private static async Task<Guid> AnswerAsync(WorkflowRunner runner, Guid runId, string requestId)
    {
        var newRunId = Guid.NewGuid();

        await DrainAsync(runner.RespondStreamingAsync(new WorkflowRespondRequest
        {
            RunId = runId,
            RequestId = requestId,
            Approved = false,
            NewRunId = newRunId,
        }));

        return newRunId;
    }

    private static async Task DrainAsync(IAsyncEnumerable<RunEvent> events)
    {
        await foreach (var _ in events)
        {
        }
    }

    private static CodeWorkflowRegistration Registration()
        => new(Name, "Asks again until the answer is yes.", _ => Build());

    private static Workflow Build()
    {
        var port = RequestPort.Create<string, bool>("approval-port");
        var portBinding = port.BindAsExecutor(allowWrappedRequests: false);

        var start = ExecutorBindingExtensions.BindAsExecutor<List<ChatMessage>, string>(
            static messages => $"Approve: {messages.LastOrDefault()?.Text ?? string.Empty}",
            id: "start");

        var decide = ExecutorBindingExtensions.BindAsExecutor<bool, string>(
            static approved => approved ? "done" : "again",
            id: "decide");

        var end = ExecutorBindingExtensions.BindAsExecutor<string, string>(
            static text => text,
            id: "end");

        return new WorkflowBuilder(start)
            .AddEdge(start, portBinding)
            .AddEdge(portBinding, decide)
            .AddEdge<string>(decide, portBinding, static text => string.Equals(text, "again", StringComparison.Ordinal))
            .AddEdge<string>(decide, end, static text => string.Equals(text, "done", StringComparison.Ordinal))
            .WithOutputFrom(end)
            .WithName(Name)
            .Build();
    }
}
