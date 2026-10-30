using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.QueuedWorkflowHarness;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A queued run of a graph built in code that its worker left half done is continued from its latest checkpoint
///The step that finished is not run again, and the continuation keeps the parameter values the run
/// started with.
/// </summary>
/// <remarks>
/// <para>
/// The twin of <c>WorkflowRecoveryTests</c> for the code graph. The recovery reads the workflow from the catalog and
/// resumes it from the checkpoint; a code graph is built again for the continuation, with new executors, and only the
/// executor ids that <c>GetWorkflowFunction</c> gives it (the functions' names) keep the checkpoint compatible. A
/// graph whose ids changed between the crash and the continuation would refuse the checkpoint.
/// </para>
/// </remarks>
public sealed class CodeWorkflowRecoveryTests
{
    private const string CodeFlow = "recover-code";
    private static readonly TimeSpan DeadLease = TimeSpan.FromMilliseconds(300);

    [Fact]
    public async Task A_code_graph_interrupted_in_its_second_step_is_continued_without_running_the_first_again()
    {
        var probe = new RecoveryProbe();

        await using var host = await StartAsync(probe, runWorker: false, configureTracon: Register);
        await using var lifetime = probe;

        using var accepted = await PostAsync(
            host,
            $"/api/workflows/{CodeFlow}/run",
            new WorkflowRunHttpRequest
            {
                Message = "episode 7",
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["tone"] = "formal" },
            });
        var headId = await AcceptedRunIdAsync(accepted);

        await StartDoomedAttemptAsync(host, probe, DeadLease);

        var job = await LeaseAsync(host, "survivor", TimeSpan.FromMinutes(5));

        job.Attempt.ShouldBe(2);

        await RunAttemptAsync(host, job, "survivor");

        probe.FirstCalls.ShouldBe(1, "the first step finished before the crash; its checkpoint covers it");
        probe.SecondCalls.ShouldBe(2, "the step that was running when the worker died runs again");
        probe.Tones.ShouldBe(["formal", "formal"]);

        var runs = await WorkflowRunsAsync(host);

        runs.Count.ShouldBe(2);

        var head = runs.Single(run => run.Id == headId);
        var continuation = runs.Single(run => run.Id != headId);

        head.Status.ShouldBe(RunStatus.Failed);
        head.Error.ShouldNotBeNull().Type.ShouldBe("interrupted");
        continuation.Status.ShouldBe(RunStatus.Completed, continuation.Error?.Message);
        continuation.ContinuedFromRunId.ShouldBe(headId);
        continuation.WorkflowName.ShouldBe(CodeFlow);

        var recorded = await host.Services.GetRequiredService<IRunInputStore>().GetAsync(TenantOf(host), continuation.Id);

        recorded.ShouldNotBeNull().Parameters.ShouldNotBeNull().ShouldContainKeyAndValue("tone", "formal");
    }

    private static void Register(ITraconBuilder builder)
        => builder.AddWorkflow(
            CodeFlow,
            static services =>
            {
                var first = services.GetWorkflowFunction(FirstStep);
                var second = services.GetWorkflowFunction(SecondStep);

                return new WorkflowBuilder(first).AddEdge(first, second).WithOutputFrom(second).WithName(CodeFlow).Build();
            },
            static options => options.Parameters =
            [
                new AgentParameter { Name = "tone", Kind = AgentParameterKind.Text, Required = false, DefaultValue = "plain" },
            ]);
}
