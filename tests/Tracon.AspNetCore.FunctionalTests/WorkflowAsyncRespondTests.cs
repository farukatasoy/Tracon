using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.QueuedWorkflowHarness;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Failure mode 5: a queued response is checked before its <c>202</c>. A response of the
/// wrong shape, a request id that is not pending and a run that is not waiting are all
/// <c>400</c>; nothing is queued for them.
/// </summary>
/// <remarks>
/// The "RequestPort + machine answer" pattern: a render finishes and the host answers
/// the pending request without holding a connection for the resumed run. The streamed
/// endpoint can refuse only what <see cref="IWorkflowRunner.ValidateResponseAsync"/>
/// judges (F-341); the queued path must refuse everything the worker would fail on, or
/// a 202 would promise a run that cannot happen.
/// </remarks>
public sealed class WorkflowAsyncRespondTests
{
    [Fact]
    public async Task A_queued_answer_resumes_the_run_in_the_worker_and_the_waiting_run_keeps_waiting()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe);
        await using var lifetime = probe;

        var (waitingId, requestId) = await RunToAwaitAsync(host);

        using var accepted = await PostAsync(host, $"/api/workflows/runs/{waitingId}/respond", new { requestId, approved = true });
        var answerId = await AcceptedRunIdAsync(accepted);

        await host.WaitForRunStatusAsync(answerId, nameof(RunStatus.Completed));

        var runs = host.Services.GetRequiredService<IRunStore>();

        // The answered run stays AwaitingInput (K-130); the answer is a new run in its session.
        (await runs.GetRunAsync(waitingId)).ShouldNotBeNull().Status.ShouldBe(RunStatus.AwaitingInput);
        (await runs.GetRunAsync(answerId)).ShouldNotBeNull().SessionId
            .ShouldBe((await runs.GetRunAsync(waitingId))!.SessionId);
    }

    [Fact]
    public async Task An_answer_of_the_wrong_shape_is_refused_with_400_and_queues_nothing()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe);
        await using var lifetime = probe;

        var (waitingId, requestId) = await RunToAwaitAsync(host);
        var before = await CountAsync(host);

        // The port takes a yes/no; a text answer cannot be read into it.
        using var refused = await PostAsync(host, $"/api/workflows/runs/{waitingId}/respond", new { requestId, text = "yes" });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        (await TraconTestHost.ReadJsonAsync(refused)).GetProperty("detail").GetString().ShouldNotBeNull()
            .ShouldContain(ApprovalPort, Case.Sensitive);
        (await CountAsync(host)).ShouldBe(before);
    }

    [Fact]
    public async Task An_answer_to_a_request_that_is_not_pending_is_refused_with_400_and_queues_nothing()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe);
        await using var lifetime = probe;

        var (waitingId, _) = await RunToAwaitAsync(host);
        var before = await CountAsync(host);

        using var refused = await PostAsync(host, $"/api/workflows/runs/{waitingId}/respond", new { requestId = "no-such-request", approved = true });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        (await TraconTestHost.ReadJsonAsync(refused)).GetProperty("detail").GetString().ShouldNotBeNull()
            .ShouldContain("no-such-request", Case.Sensitive);
        (await CountAsync(host)).ShouldBe(before);
    }

    [Fact]
    public async Task An_answer_to_a_run_that_is_not_waiting_is_refused_with_400_and_queues_nothing()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        using var first = await PostAsync(host, $"/api/workflows/{Recoverable}/run", new WorkflowRunHttpRequest { Message = "episode 7" });
        var completedId = await AcceptedRunIdAsync(first);
        await host.WaitForRunStatusAsync(completedId, nameof(RunStatus.Completed));

        var before = await CountAsync(host);

        using var refused = await PostAsync(host, $"/api/workflows/runs/{completedId}/respond", new { requestId = "any", approved = true });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        (await CountAsync(host)).ShouldBe(before);
    }

    [Fact]
    public async Task An_answer_to_an_unknown_run_is_404()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe, runWorker: false);
        await using var lifetime = probe;

        using var refused = await PostAsync(host, $"/api/workflows/runs/{Guid.NewGuid()}/respond", new { requestId = "any", approved = true });

        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await JobsAsync(host)).ShouldBeEmpty();
    }

    private static async Task<(Guid RunId, string RequestId)> RunToAwaitAsync(TraconTestHost host)
    {
        using var accepted = await PostAsync(host, $"/api/workflows/{Approval}/run", new WorkflowRunHttpRequest { Message = "the draft" });
        var runId = await AcceptedRunIdAsync(accepted);

        await host.WaitForRunStatusAsync(runId, nameof(RunStatus.AwaitingInput));

        using var listed = await host.Client.GetAsync($"/tracon/api/workflows/runs/{runId}/requests");

        return (runId, (await TraconTestHost.ReadJsonAsync(listed))[0].GetProperty("requestId").GetString()!);
    }

    private static async Task<(int Jobs, int Runs)> CountAsync(TraconTestHost host)
        => ((await JobsAsync(host)).Count, (await WorkflowRunsAsync(host)).Count);
}
