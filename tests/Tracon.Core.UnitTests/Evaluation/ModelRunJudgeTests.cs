using System.Reflection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;
using Tracon.Testing.Contracts;
using Tracon.Testing.Contracts.Judges;

namespace Tracon.Core.UnitTests.Evaluation;

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

        var score = judgment.Scores.ShouldHaveSingleItem();
        score.Name.ShouldBe("model");
        score.Kind.ShouldBe(RunScoreKind.Numeric);
        score.Value.ShouldBe(73);
        score.Comment.ShouldBe("good answer");
    }

    /// <remarks>
    /// 🚨 Deliberately out of scope, not an oversight. The built-in judge's
    /// verdict depends on the CONFIGURED MODEL, not on a versioned package's
    /// prompt, and that binding is recorded elsewhere. Stamping this judge with
    /// Tracon's own assembly version would answer a question nobody asked and
    /// make the column mean two different things.
    /// </remarks>
    [Fact]
    public async Task Does_not_stamp_an_evaluator_version()
    {
        var chatClient = new FakeChatClient(_ => Response("""{"score":73,"reason":"good answer"}"""));
        var (judge, _) = Build(chatClient);

        (await judge.JudgeAsync(Context())).EvaluatorVersion.ShouldBeNull();
    }

    [Fact]
    public async Task Null_score_stays_null()
    {
        var chatClient = new FakeChatClient(_ => Response("""{"score":null,"reason":"unclear"}"""));
        var (judge, _) = Build(chatClient);

        var judgment = await judge.JudgeAsync(Context());

        // No decision: an EMPTY judgment, so no row is written at all. It is
        // not a score of zero and it is not a null-valued row either.
        judgment.Scores.ShouldBeEmpty();
    }

    [Fact]
    public async Task Malformed_json_does_not_throw_returns_a_null_score()
    {
        var chatClient = new FakeChatClient(_ => Response("this is not JSON"));
        var (judge, _) = Build(chatClient);

        var judgment = await judge.JudgeAsync(Context());

        judgment.Scores.ShouldBeEmpty();
    }

    [Fact]
    public async Task Score_over_the_range_is_clamped()
    {
        var chatClient = new FakeChatClient(_ => Response("""{"score":150,"reason":"excessive"}"""));
        var (judge, _) = Build(chatClient);

        var judgment = await judge.JudgeAsync(Context());

        judgment.Scores.ShouldHaveSingleItem().Value.ShouldBe(100);
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

    /// <summary>
    /// The judge writes its own request options, so the provider's statement of what it sends must be asked here:
    /// a judge on a provider that drops the temperature would run on the provider's default with no word.
    /// </summary>
    [Fact]
    public async Task A_temperature_the_judges_provider_does_not_send_stops_the_judge_before_any_model_call()
    {
        var chatClient = new FakeChatClient(_ => Response("""{"score":80,"reason":"ok"}"""));
        var (judge, runs) = Build(
            chatClient,
            new ModelBinding { Provider = "test", Model = "cheap-model", Temperature = 0f, TopP = 0.9f },
            SamplingFields.None);

        var failure = await Should.ThrowAsync<TraconException>(async () => await judge.JudgeAsync(Context()));

        failure.Message.ShouldContain("ModelBinding.Temperature");
        failure.Message.ShouldContain("ModelBinding.TopP");
        failure.Message.ShouldContain("'test' provider");
        chatClient.CallCount.ShouldBe(0);
        (await runs.QueryRunsAsync(new RunQuery { TenantId = Tenant, OnlyRootRuns = false })).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_temperature_the_judges_provider_sends_is_judged_as_before()
    {
        var chatClient = new FakeChatClient(_ => Response("""{"score":80,"reason":"ok"}"""));
        var (judge, _) = Build(
            chatClient,
            new ModelBinding { Provider = "test", Model = "cheap-model", Temperature = 0f, TopP = 0.9f },
            SamplingFields.Temperature | SamplingFields.TopP);

        (await judge.JudgeAsync(Context())).Scores.ShouldHaveSingleItem().Value.ShouldBe(80);
    }

    [Fact]
    public async Task A_field_the_judge_never_sends_is_not_its_providers_to_refuse()
    {
        // The judge sends the temperature, top-p and the output limit. A seed on its binding goes nowhere, so a
        // provider that does not send seeds must not stop the judge for it.
        var chatClient = new FakeChatClient(_ => Response("""{"score":80,"reason":"ok"}"""));
        var (judge, _) = Build(
            chatClient,
            new ModelBinding { Provider = "test", Model = "cheap-model", Seed = 7 },
            SamplingFields.None);

        (await judge.JudgeAsync(Context())).Scores.ShouldHaveSingleItem().Value.ShouldBe(80);
    }

    private static (ModelRunJudge Judge, InMemoryRunStore Runs) Build(FakeChatClient chatClient)
        => Build(chatClient, new ModelBinding { Provider = "test", Model = "cheap-model" }, declared: null);

    private static (ModelRunJudge Judge, InMemoryRunStore Runs) Build(
        FakeChatClient chatClient,
        ModelBinding model,
        SamplingFields? declared)
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));

        var options = new ModelRunJudgeOptions
        {
            Model = model,
        };
        options.Criteria.Add("Is the answer correct?");

        var judge = new ModelRunJudge(
            new FixedModelProviderRegistry(chatClient, declared),
            new StaticOptionsMonitor<ModelRunJudgeOptions>(options),
            runs,
            new FixedTenantContext(Tenant),
            Options.Create(new TraconOptions()),
            NullLoggerFactory.Instance);

        return (judge, runs);
    }

    private sealed class FixedTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId => tenantId;
    }

    private sealed class FixedModelProviderRegistry(IChatClient chatClient, SamplingFields? declared = null) : IModelProviderRegistry
    {
        public IReadOnlyList<ModelProviderDescriptor> List()
            => declared is { } fields
                ? [new ModelProviderDescriptor { Name = "test", Models = [], SupportedSamplingFields = fields }]
                : [];

        public IChatClient CreateChatClient(ModelBinding binding) => chatClient;

        public ValueTask<IChatClient> CreateChatClientAsync(ModelBinding binding, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(chatClient);

        public ValueTask<IChatClient> CreateSetupChatClientAsync(ModelBinding binding, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(chatClient);

        public ValueTask<bool> HasTenantProviderOverrideAsync(ModelBinding binding, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(false);
    }
}

/// <summary>Runs the published judge contract against Tracon's built-in judge.</summary>
public sealed class ModelRunJudgeContractTests : RunJudgeContract
{
    protected override ValueTask<IRunJudge> CreateJudgeAsync()
    {
        var chatClient = new ContractChatClient();
        var tenantContext = new ContractTenantContext();
        var options = new ModelRunJudgeOptions
        {
            Model = new ModelBinding { Provider = "test", Model = "contract-model" },
        };

        IRunJudge judge = new ModelRunJudge(
            new ContractModelProviderRegistry(chatClient),
            new StaticOptionsMonitor<ModelRunJudgeOptions>(options),
            new InMemoryRunStore(tenantContext: tenantContext),
            tenantContext,
            Options.Create(new TraconOptions()),
            NullLoggerFactory.Instance);

        return ValueTask.FromResult(judge);
    }

    private sealed class ContractTenantContext : ITenantContext
    {
        public string TenantId => "contract-tenant";
    }

    private sealed class ContractModelProviderRegistry(IChatClient client) : IModelProviderRegistry
    {
        public IReadOnlyList<ModelProviderDescriptor> List() => [];

        public IChatClient CreateChatClient(ModelBinding binding) => client;

        public ValueTask<IChatClient> CreateChatClientAsync(
            ModelBinding binding,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(client);
        }

        public ValueTask<IChatClient> CreateSetupChatClientAsync(
            ModelBinding binding,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(client);
        }

        public ValueTask<bool> HasTenantProviderOverrideAsync(
            ModelBinding binding,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(false);
    }

    private sealed class ContractChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, """{"score":80,"reason":"contract"}""")));
        }

        // System.Linq.AsyncEnumerable is BCL only from net10.0; this project also runs on net8.0/net9.0.
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}

public sealed class JudgeContractCoverageTests
{
    [Fact]
    public void Every_judge_contract_has_a_derived_test()
        => ContractCoverage.MissingDerivedTypes(
            Assembly.GetExecutingAssembly(), ContractCoverage.JudgeContracts).ShouldBeEmpty();
}
