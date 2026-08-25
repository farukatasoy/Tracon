using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Evaluation;

/// <summary>Tests for the built-in model-based judge (Phase 49).</summary>
public sealed class ModelRunJudgeTests
{
    private const string Tenant = "acme";
    private const string Agent = "support";

    [Fact]
    public void Name_is_model()
        => Build(new FakeChatClient()).Judge.Name.ShouldBe("model");

    [Fact]
    public async Task Valid_json_is_parsed_correctly()
    {
        var chatClient = new FakeChatClient(_ => Response("""{"score":73,"reason":"good answer"}"""));
        var (judge, runs) = Build(chatClient);

        var judgment = await judge.JudgeAsync(Context());

        judgment.Score.ShouldBe(73);
        judgment.Reason.ShouldBe("good answer");
    }

    [Fact]
    public async Task Null_score_stays_null()
    {
        var chatClient = new FakeChatClient(_ => Response("""{"score":null,"reason":"unclear"}"""));
        var (judge, _) = Build(chatClient);

        var judgment = await judge.JudgeAsync(Context());

        judgment.Score.ShouldBeNull();
        judgment.Reason.ShouldBe("unclear");
    }

    [Fact]
    public async Task Malformed_json_does_not_throw_returns_a_null_score()
    {
        var chatClient = new FakeChatClient(_ => Response("this is not JSON"));
        var (judge, _) = Build(chatClient);

        var judgment = await judge.JudgeAsync(Context());

        judgment.Score.ShouldBeNull();
        judgment.Reason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Score_over_the_range_is_clamped()
    {
        var chatClient = new FakeChatClient(_ => Response("""{"score":150,"reason":"excessive"}"""));
        var (judge, _) = Build(chatClient);

        var judgment = await judge.JudgeAsync(Context());

        judgment.Score.ShouldBe(100);
    }

    [Fact]
    public async Task Judges_own_run_is_recorded_with_RunKind_Eval()
    {
        var chatClient = new FakeChatClient(_ => Response("""{"score":80,"reason":"ok"}"""));
        var (judge, runs) = Build(chatClient);

        await judge.JudgeAsync(Context());

        var judgeRuns = await runs.QueryRunsAsync(new RunQuery { TenantId = Tenant, OnlyRootRuns = false });

        judgeRuns.Count.ShouldBe(1);
        judgeRuns[0].Kind.ShouldBe(RunKind.Eval);
        judgeRuns[0].AgentName.ShouldBe("judge:model");
        judgeRuns[0].Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task Prompt_requests_a_structured_output_schema()
    {
        var chatClient = new FakeChatClient(_ => Response("""{"score":80,"reason":"ok"}"""));
        var (judge, _) = Build(chatClient);

        await judge.JudgeAsync(Context());

        chatClient.LastOptions.ShouldNotBeNull();
        chatClient.LastOptions!.ResponseFormat.ShouldNotBeNull();
    }

    private static ChatResponse Response(string json) => new(new ChatMessage(ChatRole.Assistant, json));

    private static RunJudgeContext Context() => new()
    {
        RunId = Guid.NewGuid(),
        TenantId = Tenant,
        AgentName = Agent,
        Input = [new ChatMessage(ChatRole.User, "question")],
        Output = "answer",
    };

    private static (ModelRunJudge Judge, InMemoryRunStore Runs) Build(FakeChatClient chatClient)
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));

        var options = new ModelRunJudgeOptions
        {
            Model = new ModelBinding { Provider = "test", Model = "cheap-model" },
        };
        options.Criteria.Add("Is the answer correct?");

        var judge = new ModelRunJudge(
            new FixedModelProviderRegistry(chatClient),
            new StaticOptionsMonitor<ModelRunJudgeOptions>(options),
            runs,
            new FixedTenantContext(Tenant),
            Options.Create(new AgentPrismOptions()),
            NullLoggerFactory.Instance);

        return (judge, runs);
    }

    private sealed class FixedTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId => tenantId;
    }

    private sealed class FixedModelProviderRegistry(IChatClient chatClient) : IModelProviderRegistry
    {
        public IReadOnlyList<ModelProviderDescriptor> List() => [];

        public IChatClient CreateChatClient(ModelBinding binding) => chatClient;

        public ValueTask<IChatClient> CreateChatClientAsync(ModelBinding binding, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(chatClient);

        public ValueTask<IChatClient> CreateSetupChatClientAsync(ModelBinding binding, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(chatClient);

        public ValueTask<bool> HasTenantProviderOverrideAsync(ModelBinding binding, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(false);
    }
}
