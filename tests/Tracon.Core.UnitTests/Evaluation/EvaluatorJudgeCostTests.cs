using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.DependencyInjection;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Evaluation;

/// <summary>
/// A bridged evaluator's model calls are recorded like the built-in judge's:
/// one <c>judge:{name}</c> run of kind <see cref="RunKind.Eval"/> that carries their usage.
/// </summary>
/// <remarks>
/// The evaluator called the judge model and nothing wrote the spend down, while
/// the documentation said its cost was added for every sampled run. The judge is built
/// through the real registration (<c>AddEvaluatorJudge</c>), because the run store
/// reaching it is a DI fact.
/// </remarks>
public sealed class EvaluatorJudgeCostTests
{
    [Fact]
    public async Task An_evaluator_judges_model_calls_are_recorded_as_one_eval_run_with_their_usage()
    {
        await using var provider = Build(new CallingEvaluator(calls: 2));

        var judge = provider.GetServices<IRunJudge>().Single(static judge => string.Equals(judge.Name, "quality", StringComparison.Ordinal));

        await judge.JudgeAsync(Context());

        var runs = await provider.GetRequiredService<IRunStore>()
            .QueryRunsAsync(new RunQuery { Take = 10, OnlyRootRuns = false });

        var run = runs.ShouldHaveSingleItem();

        run.AgentName.ShouldBe("judge:quality");
        run.Kind.ShouldBe(RunKind.Eval);
        run.Status.ShouldBe(RunStatus.Completed);
        run.Usage.ShouldNotBeNull().InputTokens.ShouldBe(2 * UsageClient.InputTokens);
        run.Usage.OutputTokens.ShouldBe(2 * UsageClient.OutputTokens);
    }

    [Fact]
    public async Task A_failing_evaluator_closes_its_run_as_failed_and_still_throws()
    {
        await using var provider = Build(new CallingEvaluator(calls: 1, throwAfter: true));

        var judge = provider.GetServices<IRunJudge>().Single(static judge => string.Equals(judge.Name, "quality", StringComparison.Ordinal));

        await Should.ThrowAsync<InvalidOperationException>(async () => await judge.JudgeAsync(Context()));

        var run = (await provider.GetRequiredService<IRunStore>()
                .QueryRunsAsync(new RunQuery { Take = 10, OnlyRootRuns = false }))
            .ShouldHaveSingleItem();

        run.Status.ShouldBe(RunStatus.Failed);
        run.Usage.ShouldNotBeNull().InputTokens.ShouldBe(UsageClient.InputTokens);
    }

    private static ServiceProvider Build(IEvaluator evaluator)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddTracon()
            .AddModelProvider(new FakeModelProvider(new UsageClient(), name: "test"))
            .AddEvaluatorJudge("quality", evaluator, static options =>
                options.Model = new ModelBinding { Provider = "test", Model = "cheap-model" });

        return services.BuildServiceProvider();
    }

    private static RunJudgeContext Context() => new()
    {
        RunId = Guid.NewGuid(),
        TenantId = "default",
        AgentName = "support",
        Input = [new ChatMessage(ChatRole.User, "question")],
        Output = "answer",
    };

    /// <summary>An evaluator that calls the judge model like the shipped catalog does.</summary>
    private sealed class CallingEvaluator(int calls, bool throwAfter = false) : IEvaluator
    {
        public IReadOnlyCollection<string> EvaluationMetricNames => ["Relevance"];

        public async ValueTask<EvaluationResult> EvaluateAsync(
            IEnumerable<ChatMessage> messages,
            ChatResponse modelResponse,
            ChatConfiguration? chatConfiguration = null,
            IEnumerable<EvaluationContext>? additionalContext = null,
            CancellationToken cancellationToken = default)
        {
            for (var call = 0; call < calls; call++)
            {
                await chatConfiguration!.ChatClient.GetResponseAsync("rate it", cancellationToken: cancellationToken);
            }

            if (throwAfter)
            {
                throw new InvalidOperationException("the evaluator failed after its model call");
            }

            return new EvaluationResult(new NumericMetric("Relevance", 4, "ok"));
        }
    }

    /// <summary>A judge model that reports usage on every answer.</summary>
    private sealed class UsageClient : IChatClient
    {
        public const int InputTokens = 30;
        public const int OutputTokens = 7;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "4"))
            {
                Usage = new UsageDetails { InputTokenCount = InputTokens, OutputTokenCount = OutputTokens, TotalTokenCount = InputTokens + OutputTokens },
            });

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
