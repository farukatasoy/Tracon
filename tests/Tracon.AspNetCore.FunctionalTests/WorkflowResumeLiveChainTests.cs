using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// An operator's resume does not join work that is still in progress: a run that is Running or Queued, or a closed run
/// whose chain of continuations ends in one, answers <c>409</c> and opens no run.
/// </summary>
/// <remarks>
/// A queued run that an interruption stopped is continued by its job: the recovery closes the interrupted run and opens a
/// continuation. A resume the operator starts from the interrupted run in the same minutes was taken for the newest link of
/// that chain, which recovery then closed as interrupted while it ran and counted against the attempts the chain has left.
/// The rule is the documented one ("resume a run after its job has ended"), held by the endpoint.
/// </remarks>
public sealed class WorkflowResumeLiveChainTests
{
    private const string Workflow = "approval-flow";

    [Theory]
    [InlineData(RunStatus.Running)]
    [InlineData(RunStatus.Queued)]
    public async Task A_run_that_is_still_in_progress_is_not_resumed(RunStatus status)
    {
        await using var host = await StartAsync();
        var inProgress = await SeedAsync(host, status);
        var rows = await CountRunsAsync(host);

        using var response = await ResumeAsync(host, inProgress);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).ShouldContain(inProgress.ToString(), Case.Insensitive);
        (await CountRunsAsync(host)).ShouldBe(rows, "a refused resume opens no run row");
    }

    [Fact]
    public async Task A_run_closed_as_interrupted_whose_continuation_is_still_running_is_not_resumed()
    {
        await using var host = await StartAsync();
        var interrupted = await SeedAsync(host, RunStatus.Failed);
        var continuation = await SeedAsync(host, RunStatus.Running, continuesFrom: interrupted);
        var rows = await CountRunsAsync(host);

        using var response = await ResumeAsync(host, interrupted);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).ShouldContain(continuation.ToString(), Case.Insensitive);
        (await CountRunsAsync(host)).ShouldBe(rows);
    }

    [Theory]
    [InlineData(RunStatus.Failed)]
    [InlineData(RunStatus.Canceled)]
    [InlineData(RunStatus.Completed)]
    public async Task A_run_whose_chain_has_ended_passes_the_check_and_meets_the_ones_after_it(RunStatus last)
    {
        await using var host = await StartAsync();
        var interrupted = await SeedAsync(host, RunStatus.Failed);

        _ = await SeedAsync(host, last, continuesFrom: interrupted);

        using var response = await ResumeAsync(host, interrupted);

        // The seeded rows have no checkpoint: the next check says so. The point is that the answer is not the conflict.
        response.StatusCode.ShouldNotBe(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
    }

    private static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(static builder => builder.UseWorkflows());

    private static async Task<Guid> SeedAsync(TraconTestHost host, RunStatus status, Guid? continuesFrom = null)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();
        var runId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = Workflow,
            Kind = RunKind.Workflow,
            WorkflowName = Workflow,
            Status = status is RunStatus.Queued ? RunStatus.Queued : RunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = "default",
            SessionId = "session-" + runId.ToString("N"),
            ContinuedFromRunId = continuesFrom,
        });

        if (status is not (RunStatus.Running or RunStatus.Queued))
        {
            await runs.CompleteRunAsync(new RunCompletion
            {
                RunId = runId,
                Status = status,
                CompletedAt = DateTimeOffset.UtcNow,
                TenantId = "default",
            });
        }

        return runId;
    }

    private static Task<HttpResponseMessage> ResumeAsync(TraconTestHost host, Guid runId)
        => host.Client.PostAsJsonAsync(
            $"/tracon/api/workflows/runs/{runId}/resume",
            new WorkflowResumeHttpRequest());

    private static async Task<int> CountRunsAsync(TraconTestHost host)
        => (await host.Services.GetRequiredService<IRunStore>()
            .QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 200 })).Count;
}
