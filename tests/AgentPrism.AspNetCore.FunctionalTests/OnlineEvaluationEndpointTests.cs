using System.Net;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>Tests for the online evaluation endpoints (Phase 49).</summary>
public sealed class OnlineEvaluationEndpointTests
{
    [Fact]
    public async Task Manual_scoring_skips_sampling_and_writes_a_score()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => services.AddSingleton<IRunJudge>(new StubJudge(77, "good")));

        var runId = await SeedScorableRunAsync(host);

        using var response = await host.Client.PostAsync(JudgeUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        var scores = body.GetProperty("scores");
        scores.GetArrayLength().ShouldBe(1);
        scores[0].GetProperty("kind").GetString().ShouldBe("Numeric");
        scores[0].GetProperty("value").GetInt32().ShouldBe(77);
        scores[0].GetProperty("source").GetString().ShouldBe("judge:stub");
        body.GetProperty("failures").GetArrayLength().ShouldBe(0);

        // The written score is also visible on the human feedback endpoint (same table).
        using var feedback = await host.Client.GetAsync(new Uri($"/agentprism/api/runs/{runId}/feedback", UriKind.Relative));
        (await AgentPrismTestHost.ReadJsonAsync(feedback)).GetArrayLength().ShouldBe(1);
    }


    [Fact]
    public async Task Manual_scoring_a_nonexistent_run_returns_404()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => services.AddSingleton<IRunJudge>(new StubJudge(77, "good")));

        using var response = await host.Client.PostAsync(JudgeUri(AgentPrismId.NewId()), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Returns_an_empty_response_when_no_judge_is_registered()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        var runId = await SeedScorableRunAsync(host);

        using var response = await host.Client.PostAsync(JudgeUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        body.GetProperty("scores").GetArrayLength().ShouldBe(0);
        body.GetProperty("failures").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Summary_endpoint_returns_zero_samples_for_an_empty_window()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/evaluation/online", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        body.GetProperty("sampleCount").GetInt64().ShouldBe(0);
        body.GetProperty("belowThreshold").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Summary_endpoint_shows_the_sample_after_manual_scoring()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => services.AddSingleton<IRunJudge>(new StubJudge(20, "bad")));

        var runId = await SeedScorableRunAsync(host);
        await host.Client.PostAsync(JudgeUri(runId), content: null);

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/evaluation/online", UriKind.Relative));
        var body = await AgentPrismTestHost.ReadJsonAsync(response);

        body.GetProperty("sampleCount").GetInt64().ShouldBe(1);
        body.GetProperty("averageScore").GetDouble().ShouldBe(20);
    }

    private static Uri JudgeUri(Guid runId) => new($"/agentprism/api/runs/{runId}/judge", UriKind.Relative);

    private static async Task<Guid> SeedScorableRunAsync(AgentPrismTestHost host)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();
        var inputs = host.Services.GetRequiredService<IRunInputStore>();
        var runId = AgentPrismId.NewId();
        var now = DateTimeOffset.UtcNow;

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = now,
            TenantId = "default",
        });

        await inputs.SaveAsync(new RunInputRecord
        {
            RunId = runId,
            TenantId = "default",
            Messages = [new ChatMessage(ChatRole.User, "question")],
            CreatedAt = now,
        });

        await runs.AppendEventAsync(new RunEvent
        {
            RunId = runId,
            Sequence = 0,
            Type = RunEventType.MessageCompleted,
            Timestamp = now,
            Text = "answer",
        });

        await runs.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = now,
        });

        return runId;
    }

    private sealed class StubJudge(int score, string reason) : IRunJudge
    {
        public string Name => "stub";

        public ValueTask<RunJudgment> JudgeAsync(RunJudgeContext context, CancellationToken cancellationToken = default)
            => new(new RunJudgment { Score = score, Reason = reason });
    }
}
