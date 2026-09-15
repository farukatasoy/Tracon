using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Evaluation;

/// <summary>
/// Tests for the bridge that turns a Microsoft.Extensions.AI
/// <see cref="IEvaluator"/> into a Tracon run judge (Phase 155).
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

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await judge.JudgeAsync(Context()));

        exception.Message.ShouldContain("quality");
    }

    [Fact]
    public async Task The_evaluators_OWN_assembly_version_is_stamped_on_the_judgment()
    {
        // ScriptedEvaluator lives in this test assembly, so the bridge must
        // report THIS assembly's version -- it asks the evaluator's own
        // assembly and opens no privileged path for the shipped catalog.
        var expected = typeof(ScriptedEvaluator).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        expected.ShouldNotBeNullOrWhiteSpace();

        var judge = Build("quality", new ScriptedEvaluator(new NumericMetric("Relevance", 4, "ok")));

        (await judge.JudgeAsync(Context())).EvaluatorVersion.ShouldBe(expected);
    }

    /// <remarks>
    /// 🚨 The failure this guards is the whole point of the stamp being
    /// OBSERVABILITY: an evaluator whose assembly carries no informational
    /// version must still be scored. Losing a tenant's score rows to a missing
    /// attribute would be a far worse bug than a missing version.
    /// </remarks>
    [Fact]
    public async Task An_evaluator_with_NO_assembly_version_is_still_scored()
    {
        var judge = Build("quality", EvaluatorInAssemblyWithVersion(null, new NumericMetric("Relevance", 4, "ok")));

        var judgment = await judge.JudgeAsync(Context());

        judgment.EvaluatorVersion.ShouldBeNull();
        judgment.Scores.ShouldHaveSingleItem().Name.ShouldBe("quality.Relevance");
    }

    /// <remarks>
    /// 🚨 Measures "resolved ONCE, at construction", not "resolved correctly".
    /// The observable is the Debug line the versionless path writes: it is
    /// already there before the first call (so the read happened at
    /// construction), and three calls do not add a second one (so no call pays
    /// for the reflection). A per-call read would make this three or four.
    /// </remarks>
    [Fact]
    public async Task The_version_is_resolved_ONCE_at_construction_not_per_call()
    {
        var logger = new RecordingLogger();
        var evaluator = EvaluatorInAssemblyWithVersion(null, new NumericMetric("Relevance", 4, "ok"));

        var judge = new EvaluatorRunJudge(
            "quality",
            evaluator,
            new FixedModelProviderRegistry(),
            new StaticOptionsMonitor<ModelRunJudgeOptions>(new ModelRunJudgeOptions
            {
                Model = new ModelBinding { Provider = "test", Model = "cheap-model" },
            }),
            logger);

        logger.VersionLines.ShouldBe(1, "the version is read when the judge is built, before any call");

        await judge.JudgeAsync(Context());
        await judge.JudgeAsync(Context());
        await judge.JudgeAsync(Context());

        logger.VersionLines.ShouldBe(1, "three calls must not repeat the read");
    }

    [Fact]
    public async Task An_over_long_assembly_version_is_truncated_rather_than_failing_the_judgment()
    {
        // A contract failure would write NO rows at all. Truncating keeps the
        // scores and loses only the tail of an unusually long version string.
        var judge = Build("quality", EvaluatorInAssemblyWithVersion(
            new string('9', RunScoreRules.MaxEvaluatorVersionLength + 40),
            new NumericMetric("Relevance", 4, "ok")));

        var judgment = await judge.JudgeAsync(Context());

        judgment.EvaluatorVersion.ShouldNotBeNull().Length.ShouldBe(RunScoreRules.MaxEvaluatorVersionLength);
        judgment.Scores.ShouldHaveSingleItem();
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

    /// <summary>
    /// Builds an <see cref="IEvaluator"/> whose type lives in a DYNAMIC assembly
    /// carrying exactly the informational version asked for -- or none at all.
    /// </summary>
    /// <remarks>
    /// 🚨 A dynamic assembly is the only honest way to reach these two shapes.
    /// Every assembly compiled in this repository has a short informational
    /// version, so neither "no version" nor "an over-long version" can be
    /// produced with an ordinary class, and pointing the bridge at a stand-in
    /// would test the stand-in rather than the assembly read.
    /// </remarks>
    /// <param name="informationalVersion">
    /// The version to stamp on the generated assembly, or <see langword="null"/>
    /// to stamp none.
    /// </param>
    /// <param name="metrics">The metrics the evaluator reports.</param>
    private static ProbeEvaluatorBase EvaluatorInAssemblyWithVersion(
        string? informationalVersion,
        params EvaluationMetric[] metrics)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"Tracon.VersionProbe.{Guid.NewGuid():N}"),
            AssemblyBuilderAccess.Run);

        if (informationalVersion is not null)
        {
            assembly.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!,
                [informationalVersion]));
        }

        // No constructor is defined, so CreateType() emits the default one that
        // chains to the base class's parameterless constructor.
        var type = assembly
            .DefineDynamicModule("main")
            .DefineType("ProbeEvaluator", TypeAttributes.Public, typeof(ProbeEvaluatorBase))
            .CreateType();

        var evaluator = (ProbeEvaluatorBase)Activator.CreateInstance(type)!;
        evaluator.Metrics = metrics;
        return evaluator;
    }

    /// <summary>The base the generated probe type derives from.</summary>
    /// <remarks>
    /// Only the TYPE is generated; the behaviour lives here in ordinary C#, so
    /// the probe needs no emitted method bodies. What matters to the test is
    /// that <c>GetType().Assembly</c> is the generated one.
    /// </remarks>
    public class ProbeEvaluatorBase : IEvaluator
    {
        public EvaluationMetric[] Metrics { get; set; } = [];

        public IReadOnlyCollection<string> EvaluationMetricNames
            => [.. Metrics.Select(static metric => metric.Name)];

        public ValueTask<EvaluationResult> EvaluateAsync(
            IEnumerable<ChatMessage> messages,
            ChatResponse modelResponse,
            ChatConfiguration? chatConfiguration = null,
            IEnumerable<EvaluationContext>? additionalContext = null,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new EvaluationResult(Metrics));
    }

    /// <summary>Counts the Debug lines the version resolution writes.</summary>
    private sealed class RecordingLogger : ILogger<EvaluatorRunJudge>
    {
        public int VersionLines { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (formatter(state, exception).Contains("informational version", StringComparison.Ordinal))
            {
                VersionLines++;
            }
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
