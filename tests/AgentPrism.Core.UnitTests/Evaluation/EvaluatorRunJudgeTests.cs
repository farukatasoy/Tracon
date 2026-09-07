using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Evaluation;

/// <summary>
/// Tests for the bridge that turns a Microsoft.Extensions.AI
/// <see cref="IEvaluator"/> into an AgentPrism run judge (Phase 155).
/// </summary>
public sealed class EvaluatorRunJudgeTests
{
    [Fact]
    public async Task Every_metric_becomes_its_own_score_named_after_the_judge()
    {
        var judge = Build("relevance", new ScriptedEvaluator(
            new NumericMetric("Relevance", 4, "on topic"),
            new NumericMetric("Coherence", 5, "reads well")));

        var judgment = await judge.JudgeAsync(Context());

        judgment.Scores.Select(static score => score.Name)
            .ShouldBe(["relevance.Relevance", "relevance.Coherence"], ignoreOrder: true);
        judgment.Scores.ShouldAllBe(static score => score.Kind == RunScoreKind.Numeric);
    }

    [Fact]
    public async Task A_bridged_score_is_never_the_judges_headline_score()
    {
        // The prefix is what keeps a 1-5 evaluator metric out of the 0-100
        // online-evaluation average: the handler feeds the window only from a
        // score named exactly after the judge, and a prefixed name never is.
        var judge = Build("relevance", new ScriptedEvaluator(new NumericMetric("Relevance", 4, "on topic")));

        var judgment = await judge.JudgeAsync(Context());

        var judgeName = judge.Name;
        judgment.Scores.ShouldAllBe(score => !string.Equals(score.Name, judgeName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Two_evaluators_reporting_the_same_metric_do_not_collide()
    {
        var first = Build("first", new ScriptedEvaluator(new NumericMetric("Relevance", 4, "a")));
        var second = Build("second", new ScriptedEvaluator(new NumericMetric("Relevance", 2, "b")));

        var firstJudgment = await first.JudgeAsync(Context());
        var secondJudgment = await second.JudgeAsync(Context());

        firstJudgment.Scores.ShouldHaveSingleItem().Name.ShouldBe("first.Relevance");
        secondJudgment.Scores.ShouldHaveSingleItem().Name.ShouldBe("second.Relevance");
    }

    [Fact]
    public async Task A_metric_with_no_measurement_writes_null_not_zero()
    {
        var judge = Build("quality", new ScriptedEvaluator(
            new NumericMetric("Relevance", value: null, "the evaluator could not decide")));

        var judgment = await judge.JudgeAsync(Context());

        var score = judgment.Scores.ShouldHaveSingleItem();
        score.Value.ShouldBeNull();
        score.Comment.ShouldBe("the evaluator could not decide");
    }

    [Fact]
    public async Task A_boolean_metric_becomes_a_binary_score()
    {
        var judge = Build("quality", new ScriptedEvaluator(
            new BooleanMetric("Grounded", true, "supported"),
            new BooleanMetric("Refused", false, "answered"),
            new BooleanMetric("Unknown", value: null, "no measurement")));

        var judgment = await judge.JudgeAsync(Context());

        judgment.Scores.ShouldAllBe(static score => score.Kind == RunScoreKind.Binary);
        Value(judgment, "quality.Grounded").ShouldBe(1);
        Value(judgment, "quality.Refused").ShouldBe(0);
        Value(judgment, "quality.Unknown").ShouldBeNull();
    }

    [Fact]
    public async Task A_string_metric_becomes_a_categorical_score()
    {
        var judge = Build("quality", new ScriptedEvaluator(new StringMetric("Verdict", "Good", "graded")));

        var judgment = await judge.JudgeAsync(Context());

        var score = judgment.Scores.ShouldHaveSingleItem();
        score.Kind.ShouldBe(RunScoreKind.Categorical);
        score.TextValue.ShouldBe("Good");
        score.Value.ShouldBeNull();
    }

    [Fact]
    public async Task An_empty_string_metric_is_skipped_because_it_cannot_be_stored()
    {
        // A categorical score with no text breaks the stored invariant, and
        // there is no "null categorical" to fall back on.
        var judge = Build("quality", new ScriptedEvaluator(
            new StringMetric("Verdict", value: null, "nothing to say"),
            new NumericMetric("Relevance", 3, "fine")));

        var judgment = await judge.JudgeAsync(Context());

        judgment.Scores.ShouldHaveSingleItem().Name.ShouldBe("quality.Relevance");
    }

    [Fact]
    public async Task An_over_long_categorical_value_is_truncated_to_the_stored_limit()
    {
        var judge = Build("quality", new ScriptedEvaluator(
            new StringMetric("Verdict", new string('x', RunScoreRules.MaxTextValueLength + 50), "long")));

        var judgment = await judge.JudgeAsync(Context());

        judgment.Scores.ShouldHaveSingleItem().TextValue.ShouldNotBeNull()
            .Length.ShouldBe(RunScoreRules.MaxTextValueLength);
    }

    [Fact]
    public async Task A_metric_name_that_cannot_be_a_score_name_is_skipped()
    {
        var judge = Build("quality", new ScriptedEvaluator(
            new NumericMetric("Tool Call Accuracy", 3, "spaces are not legal"),
            new NumericMetric("Relevance", 3, "fine")));

        var judgment = await judge.JudgeAsync(Context());

        judgment.Scores.ShouldHaveSingleItem().Name.ShouldBe("quality.Relevance");
    }

    [Fact]
    public async Task Diagnostics_reach_the_comment_so_a_null_value_is_explained()
    {
        var metric = new NumericMetric("Relevance", value: null, reason: null);
        metric.Diagnostics = [EvaluationDiagnostic.Error("the conversation carried no tool calls")];

        var judge = Build("quality", new ScriptedEvaluator(metric));

        var judgment = await judge.JudgeAsync(Context());

        var score = judgment.Scores.ShouldHaveSingleItem();
        score.Value.ShouldBeNull();
        score.Comment.ShouldNotBeNull().ShouldContain("the conversation carried no tool calls");
    }

    [Fact]
    public async Task An_evaluator_with_no_metrics_produces_an_empty_judgment()
    {
        var judge = Build("quality", new ScriptedEvaluator());

        (await judge.JudgeAsync(Context())).Scores.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_call_budget_reaches_the_evaluator()
    {
        // 🚨 Asserted on the token the EVALUATOR received, not on the call
        // throwing: a pre-cancelled token trips the judge's own guard long
        // before EvaluateAsync, so a judge that dropped the token entirely
        // would still throw and the test would still pass.
        var evaluator = new ScriptedEvaluator(new NumericMetric("Relevance", 4, "ok"));
        var judge = Build("quality", evaluator);
        using var cancellation = new CancellationTokenSource();

        await judge.JudgeAsync(Context(), cancellation.Token);

        evaluator.LastToken.ShouldBe(cancellation.Token);
    }

    [Fact]
    public async Task A_token_cancelled_inside_the_evaluator_is_honoured()
    {
        var evaluator = new ScriptedEvaluator(new NumericMetric("Relevance", 4, "ok"));
        using var cancellation = new CancellationTokenSource();
        evaluator.OnEvaluate = () => cancellation.Cancel();
        var judge = Build("quality", evaluator);

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await judge.JudgeAsync(Context(), cancellation.Token));
    }

    [Fact]
    public async Task The_run_output_is_what_the_evaluator_grades()
    {
        var evaluator = new ScriptedEvaluator(new NumericMetric("Relevance", 4, "ok"));
        var judge = Build("quality", evaluator);

        await judge.JudgeAsync(Context() with { Output = "the answer" });

        evaluator.LastResponse!.Text.ShouldBe("the answer");
        evaluator.LastMessages!.ShouldHaveSingleItem().Text.ShouldBe("question");
    }

    [Fact]
    public async Task A_missing_judge_model_fails_with_a_named_error()
    {
        var judge = new EvaluatorRunJudge(
            "quality",
            new ScriptedEvaluator(),
            new FixedModelProviderRegistry(),
            new StaticOptionsMonitor<ModelRunJudgeOptions>(new ModelRunJudgeOptions { Model = null! }),
            NullLogger<EvaluatorRunJudge>.Instance);

        var exception = await Should.ThrowAsync<AgentPrismException>(
            async () => await judge.JudgeAsync(Context()));

        exception.Message.ShouldContain("quality");
    }

    private static double? Value(RunJudgment judgment, string name)
        => judgment.Scores.Single(score => string.Equals(score.Name, name, StringComparison.Ordinal)).Value;

    private static RunJudgeContext Context() => new()
    {
        RunId = Guid.NewGuid(),
        TenantId = "acme",
        AgentName = "support",
        Input = [new ChatMessage(ChatRole.User, "question")],
        Output = "answer",
    };

    private static EvaluatorRunJudge Build(string name, IEvaluator evaluator)
        => new(
            name,
            evaluator,
            new FixedModelProviderRegistry(),
            new StaticOptionsMonitor<ModelRunJudgeOptions>(new ModelRunJudgeOptions
            {
                Model = new ModelBinding { Provider = "test", Model = "cheap-model" },
            }),
            NullLogger<EvaluatorRunJudge>.Instance);

    private sealed class ScriptedEvaluator(params EvaluationMetric[] metrics) : IEvaluator
    {
        public IReadOnlyCollection<string> EvaluationMetricNames
            => [.. metrics.Select(static metric => metric.Name)];

        public IEnumerable<ChatMessage>? LastMessages { get; private set; }

        public ChatResponse? LastResponse { get; private set; }

        public CancellationToken LastToken { get; private set; }

        public Action? OnEvaluate { get; set; }

        public ValueTask<EvaluationResult> EvaluateAsync(
            IEnumerable<ChatMessage> messages,
            ChatResponse modelResponse,
            ChatConfiguration? chatConfiguration = null,
            IEnumerable<EvaluationContext>? additionalContext = null,
            CancellationToken cancellationToken = default)
        {
            LastToken = cancellationToken;
            LastMessages = messages;
            LastResponse = modelResponse;
            OnEvaluate?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new EvaluationResult(metrics));
        }
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class FixedModelProviderRegistry : IModelProviderRegistry, IDisposable
    {
        private readonly StubChatClient _client = new();

        public void Dispose() => _client.Dispose();

        public IReadOnlyList<ModelProviderDescriptor> List() => [];

        public IChatClient CreateChatClient(ModelBinding binding) => _client;

        public ValueTask<IChatClient> CreateChatClientAsync(ModelBinding binding, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IChatClient>(_client);

        public ValueTask<IChatClient> CreateSetupChatClientAsync(ModelBinding binding, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IChatClient>(_client);
        }

        public ValueTask<bool> HasTenantProviderOverrideAsync(ModelBinding binding, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(false);
    }

    private sealed class StubChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "stub")));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
