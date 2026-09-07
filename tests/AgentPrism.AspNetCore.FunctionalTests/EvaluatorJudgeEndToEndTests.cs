using System.Net;
using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Proves the evaluator bridge across the store boundary (Phase 155): what a
/// judge returns, what reaches <c>run_scores</c>, and what a caller reads back.
/// A unit test cannot answer these — the uniqueness key, the write and the read
/// all live on the other side of the store.
/// </summary>
public sealed class EvaluatorJudgeEndToEndTests
{
    [Fact]
    public async Task Each_metric_becomes_its_own_score_row()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: static agentPrism => agentPrism.AddEvaluatorJudge(
                "quality",
                new ScriptedEvaluator(
                    new NumericMetric("Relevance", 4, "on topic"),
                    new NumericMetric("Coherence", 5, "reads well")),
                JudgeModel));

        var runId = await SeedScorableRunAsync(host);

        using var response = await host.Client.PostAsync(JudgeUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var scores = await ReadScoresAsync(host, runId);
        scores.Count.ShouldBe(2);
        Score(scores, "quality.Relevance").GetProperty("value").GetDouble().ShouldBe(4);
        Score(scores, "quality.Coherence").GetProperty("value").GetDouble().ShouldBe(5);
        scores.ShouldAllBe(static score =>
            score.GetProperty("source").GetString() == "judge:quality");
    }

    [Fact]
    public async Task Two_evaluators_reporting_the_same_metric_both_survive()
    {
        // The score name is part of the uniqueness key, so without the judge
        // prefix the second write would overwrite the first and one metric
        // would silently disappear.
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: static agentPrism => agentPrism
                .AddEvaluatorJudge("first", new ScriptedEvaluator(new NumericMetric("Relevance", 4, "a")), JudgeModel)
                .AddEvaluatorJudge("second", new ScriptedEvaluator(new NumericMetric("Relevance", 1, "b"))));

        var runId = await SeedScorableRunAsync(host);

        await host.Client.PostAsync(JudgeUri(runId), content: null);

        var scores = await ReadScoresAsync(host, runId);
        scores.Count.ShouldBe(2);
        Score(scores, "first.Relevance").GetProperty("value").GetDouble().ShouldBe(4);
        Score(scores, "second.Relevance").GetProperty("value").GetDouble().ShouldBe(1);
    }

    [Fact]
    public async Task A_metric_with_no_measurement_is_stored_as_null_not_zero()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: static agentPrism => agentPrism.AddEvaluatorJudge(
                "quality",
                new ScriptedEvaluator(new NumericMetric("Relevance", value: null, "could not decide")),
                JudgeModel));

        var runId = await SeedScorableRunAsync(host);

        await host.Client.PostAsync(JudgeUri(runId), content: null);

        var score = Score(await ReadScoresAsync(host, runId), "quality.Relevance");
        score.GetProperty("value").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_throwing_evaluator_is_reported_and_does_not_take_the_other_judge_down()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: static agentPrism => agentPrism
                .AddEvaluatorJudge("broken", new ThrowingEvaluator(), JudgeModel)
                .AddEvaluatorJudge("working", new ScriptedEvaluator(new NumericMetric("Relevance", 3, "fine"))));

        var runId = await SeedScorableRunAsync(host);

        using var response = await host.Client.PostAsync(JudgeUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        var failures = body.GetProperty("failures").EnumerateArray().ToList();
        failures.ShouldHaveSingleItem().GetProperty("judgeName").GetString().ShouldBe("broken");

        var scores = await ReadScoresAsync(host, runId);
        scores.ShouldHaveSingleItem().GetProperty("name").GetString().ShouldBe("working.Relevance");
    }

    [Fact]
    public async Task A_bridged_metric_never_reaches_the_zero_to_hundred_online_average()
    {
        // A calibrated evaluator reports on a 1-5 scale. If it fed the window,
        // an average of 4 would sit far below the low-score threshold and the
        // alarm would fire on a perfectly good run.
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: static agentPrism => agentPrism.AddEvaluatorJudge(
                "quality",
                new ScriptedEvaluator(new NumericMetric("Relevance", 4, "on topic")),
                JudgeModel));

        var runId = await SeedScorableRunAsync(host);
        await host.Client.PostAsync(JudgeUri(runId), content: null);

        using var summary = await host.Client.GetAsync(new Uri("/agentprism/api/evaluation/online", UriKind.Relative));
        var body = await AgentPrismTestHost.ReadJsonAsync(summary);

        body.GetProperty("sampleCount").GetInt64().ShouldBe(0);
        body.GetProperty("belowThreshold").GetBoolean().ShouldBeFalse();

        // The score itself is stored and readable; only the average excludes it.
        (await ReadScoresAsync(host, runId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Registering_no_evaluator_leaves_the_run_exactly_as_it_was()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        var runId = await SeedScorableRunAsync(host);

        using var response = await host.Client.PostAsync(JudgeUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        body.GetProperty("scores").GetArrayLength().ShouldBe(0);
        body.GetProperty("failures").GetArrayLength().ShouldBe(0);
        (await ReadScoresAsync(host, runId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_calibrated_catalog_evaluator_writes_a_row_under_its_own_metric_name()
    {
        // The real RelevanceEvaluator from Microsoft.Extensions.AI.Evaluation.Quality,
        // driven by the host's fake model. Its answer is not a graded response, so
        // it reports no measurement -- and that is the point: the metric NAME comes
        // from the shipped catalog, not from a stub, and it reaches the store.
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: static agentPrism => agentPrism.AddEvaluatorJudge(
                "relevance",
                new RelevanceEvaluator(),
                JudgeModel));

        var runId = await SeedScorableRunAsync(host);

        using var response = await host.Client.PostAsync(JudgeUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var scores = await ReadScoresAsync(host, runId);
        var names = scores.Select(static score => score.GetProperty("name").GetString()).ToList();

        names.ShouldNotBeEmpty();
        names.ShouldAllBe(static name => name!.StartsWith("relevance.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_judge_name_that_cannot_be_a_score_name_is_refused_at_registration()
    {
        await Should.ThrowAsync<ArgumentException>(async () =>
            await AgentPrismTestHost.StartAsync(
                configureAgentPrism: static agentPrism => agentPrism.AddEvaluatorJudge(
                    "not a name",
                    new ScriptedEvaluator(),
                    JudgeModel)));
    }

    private static void JudgeModel(ModelRunJudgeOptions options)
        => options.Model = new ModelBinding { Provider = "echo", Model = "echo-model" };

    private static JsonElement Score(IReadOnlyList<JsonElement> scores, string name)
        => scores.Single(score => string.Equals(score.GetProperty("name").GetString(), name, StringComparison.Ordinal));

    private static async Task<IReadOnlyList<JsonElement>> ReadScoresAsync(AgentPrismTestHost host, Guid runId)
    {
        using var response = await host.Client.GetAsync(
            new Uri($"/agentprism/api/runs/{runId}/feedback", UriKind.Relative));

        return [.. (await AgentPrismTestHost.ReadJsonAsync(response)).EnumerateArray()];
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

    private sealed class ScriptedEvaluator(params EvaluationMetric[] metrics) : IEvaluator
    {
        public IReadOnlyCollection<string> EvaluationMetricNames
            => [.. metrics.Select(static metric => metric.Name)];

        public ValueTask<EvaluationResult> EvaluateAsync(
            IEnumerable<ChatMessage> messages,
            ChatResponse modelResponse,
            ChatConfiguration? chatConfiguration = null,
            IEnumerable<EvaluationContext>? additionalContext = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new EvaluationResult(metrics));
        }
    }

    private sealed class ThrowingEvaluator : IEvaluator
    {
        public IReadOnlyCollection<string> EvaluationMetricNames => ["Broken"];

        public ValueTask<EvaluationResult> EvaluateAsync(
            IEnumerable<ChatMessage> messages,
            ChatResponse modelResponse,
            ChatConfiguration? chatConfiguration = null,
            IEnumerable<EvaluationContext>? additionalContext = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the evaluator failed");
    }
}
