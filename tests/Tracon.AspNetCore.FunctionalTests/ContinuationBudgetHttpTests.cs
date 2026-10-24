using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.NodeUsageHost;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The ceiling of a run tree is the logical run's, over the real host: a person's answer and an operator's resume
/// start from what the chain spent, not from zero (F-531).
/// </summary>
/// <remarks>
/// <para>
/// A workflow with a human approval loop got the whole ceiling again with every answer, because each answer is a new
/// run row with a new budget; a run that failed for its budget was resumed into the node that had been refused. The
/// runner-level cases (<c>ContinuationBudgetTests</c>) prove the arithmetic; these prove it across the boundaries that
/// carry it: the HTTP endpoints, the run store, the container-built runner and the agent step's real model pipeline.
/// </para>
/// </remarks>
public sealed class ContinuationBudgetHttpTests
{
    [Fact]
    public async Task An_answer_over_HTTP_is_not_given_a_writer_turn_the_chain_has_no_tokens_left_for()
    {
        // Ten tokens per turn and a ceiling of ten: the first draft spends the chain's tokens. The reviewer sends
        // it back, and the answer's writer step is a new run with a new budget - before the fix, a second draft.
        var provider = ReviewAndReviseHarness.Writing()
            .RespondsWith("the first draft", inputTokens: 4, outputTokens: 6)
            .RespondsWith("the second draft", inputTokens: 4, outputTokens: 6);

        var harness = new ReviewAndReviseHarness(provider);

        await using var host = await harness.StartAsync(
            configureServices: services => services.Configure<TraconOptions>(options => options.AgentGraph.MaxTotalTokens = 10));

        var (waiting, _) = await ReviewAndReviseHarness.StartRunAsync(host);

        (await RecordAsync(host, waiting)).Status.ShouldBe(RunStatus.AwaitingInput);

        var (answer, _) = await ReviewAndReviseHarness.AnswerAsync(host, waiting, new ReviewAndReviseHarness.ReviewDecision(false, "make it shorter"));
        var record = await RecordAsync(host, answer);

        record.Status.ShouldBe(RunStatus.Failed);

        var error = record.Error.ShouldNotBeNull();

        error.Type.ShouldBe(TraconRunBudgetExceededException.RunBudgetExceededErrorType);
        error.Class.ShouldBe(RunErrorClass.QuotaExceeded);
        error.Message.ShouldContain("token budget is exhausted (10/10)", Case.Sensitive);
        provider.Requests.Count.ShouldBe(1, "the writer was given a second turn on a budget of zero");
    }

    [Fact]
    public async Task An_answer_over_HTTP_may_use_what_the_chain_left()
    {
        var provider = ReviewAndReviseHarness.Writing()
            .RespondsWith("the first draft", inputTokens: 4, outputTokens: 6)
            .RespondsWith("the second draft", inputTokens: 4, outputTokens: 6);

        var harness = new ReviewAndReviseHarness(provider);

        await using var host = await harness.StartAsync(
            configureServices: services => services.Configure<TraconOptions>(options => options.AgentGraph.MaxTotalTokens = 100));

        var (waiting, _) = await ReviewAndReviseHarness.StartRunAsync(host);
        var (answer, _) = await ReviewAndReviseHarness.AnswerAsync(host, waiting, new ReviewAndReviseHarness.ReviewDecision(false, "make it shorter"));

        (await RecordAsync(host, answer)).Status.ShouldBe(RunStatus.AwaitingInput);
        provider.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task An_operator_resume_over_HTTP_is_refused_at_the_node_that_was_refused_and_goes_on_once_the_ceiling_is_raised()
    {
        var synthesized = new Calls();

        await using var host = await StartAsync(
            builder => builder
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("speak", Speaks(1.2m))
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("synthesize", Counts(synthesized)),
            configureOptions: options => options.AgentGraph.MaxTotalCost = 1m);

        await SaveAsync(host, Workflow, Function("speak"), Function("synthesize"));

        var failed = await RunAsync(host);

        (await RecordAsync(host, failed)).Error.ShouldNotBeNull().Type.ShouldBe(TraconRunBudgetExceededException.RunBudgetExceededErrorType);

        // The operator resumes it: refused at the same node, as a run that says what it continues.
        var refused = await RecordAsync(host, await ResumeAsync(host, failed));

        refused.Status.ShouldBe(RunStatus.Failed);
        refused.Error.ShouldNotBeNull().Type.ShouldBe(TraconRunBudgetExceededException.RunBudgetExceededErrorType);
        refused.Error.Message.ShouldContain("cost budget is exhausted (1.200000/1.000000)", Case.Sensitive);
        refused.ContinuedFromRunId.ShouldBe(failed);
        synthesized.Count.ShouldBe(0);

        // The chain is walked forward over HTTP.
        using var forward = await host.Client.GetAsync($"/tracon/api/runs?continuedFromRunId={failed}");
        (await TraconTestHost.ReadJsonAsync(forward)).EnumerateArray()
            .Select(static run => run.GetProperty("id").GetGuid())
            .ShouldBe([refused.Id]);

        // The ceiling is a setting.
        host.Services.GetRequiredService<IOptions<TraconOptions>>().Value.AgentGraph.MaxTotalCost = 5m;

        var goneOn = await RecordAsync(host, await ResumeAsync(host, failed));

        goneOn.Status.ShouldBe(RunStatus.Completed, goneOn.Error?.Message);
        synthesized.Count.ShouldBe(1);
    }

    private static async Task<Guid> ResumeAsync(TraconTestHost host, Guid runId)
    {
        using var response = await host.Client.PostAsJsonAsync($"/tracon/api/workflows/runs/{runId}/resume", new WorkflowResumeHttpRequest());

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return WorkflowResponseHarness.RunIdOf((await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync())).ToList());
    }

    private static Func<IServiceProvider, Func<List<ChatMessage>, Microsoft.Agents.AI.Workflows.IWorkflowContext, CancellationToken, ValueTask<List<ChatMessage>>>> Speaks(decimal amount)
        => _ => async (messages, context, _) =>
        {
            (await context.ReportUsageAsync(Speech(amount))).ShouldBeTrue();

            return messages;
        };

    private static Func<IServiceProvider, Func<List<ChatMessage>, Microsoft.Agents.AI.Workflows.IWorkflowContext, CancellationToken, ValueTask<List<ChatMessage>>>> Counts(Calls calls)
        => _ => (messages, _, _) =>
        {
            calls.Hit();

            return new ValueTask<List<ChatMessage>>(messages);
        };

    private sealed class Calls
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Hit() => Interlocked.Increment(ref _count);
    }
}
