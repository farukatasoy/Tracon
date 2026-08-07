using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Evaluation;

/// <summary>Yerlesik model tabanli yargicin testleri (Faz 49).</summary>
public sealed class ModelRunJudgeTests
{
    private const string Tenant = "acme";
    private const string Agent = "support";

    [Fact]
    public void Name_model_dir()
        => Build(new FakeChatClient()).Judge.Name.ShouldBe("model");

    [Fact]
    public async Task Gecerli_json_dogru_ayristirilir()
    {
        var chatClient = new FakeChatClient(_ => Response("""{"score":73,"reason":"iyi cevap"}"""));
        var (judge, runs) = Build(chatClient);

        var judgment = await judge.JudgeAsync(Context());

        judgment.Score.ShouldBe(73);
        judgment.Reason.ShouldBe("iyi cevap");
    }

    [Fact]
    public async Task Null_score_null_olarak_kalir()
    {
        var chatClient = new FakeChatClient(_ => Response("""{"score":null,"reason":"belirsiz"}"""));
        var (judge, _) = Build(chatClient);

        var judgment = await judge.JudgeAsync(Context());

        judgment.Score.ShouldBeNull();
        judgment.Reason.ShouldBe("belirsiz");
    }

    [Fact]
    public async Task Bozuk_json_hata_firlatmaz_null_puan_doner()
    {
        var chatClient = new FakeChatClient(_ => Response("bu JSON degil"));
        var (judge, _) = Build(chatClient);

        var judgment = await judge.JudgeAsync(Context());

        judgment.Score.ShouldBeNull();
        judgment.Reason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Araligi_asan_puan_kirpilir()
    {
        var chatClient = new FakeChatClient(_ => Response("""{"score":150,"reason":"asiri"}"""));
        var (judge, _) = Build(chatClient);

        var judgment = await judge.JudgeAsync(Context());

        judgment.Score.ShouldBe(100);
    }

    [Fact]
    public async Task Yargicin_kendi_calistirmasi_RunKind_Eval_ile_kaydedilir()
    {
        var chatClient = new FakeChatClient(_ => Response("""{"score":80,"reason":"tamam"}"""));
        var (judge, runs) = Build(chatClient);

        await judge.JudgeAsync(Context());

        var judgeRuns = await runs.QueryRunsAsync(new RunQuery { TenantId = Tenant, OnlyRootRuns = false });

        judgeRuns.Count.ShouldBe(1);
        judgeRuns[0].Kind.ShouldBe(RunKind.Eval);
        judgeRuns[0].AgentName.ShouldBe("judge:model");
        judgeRuns[0].Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task Istem_yapilandirilmis_cikti_semasi_ister()
    {
        var chatClient = new FakeChatClient(_ => Response("""{"score":80,"reason":"tamam"}"""));
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
        Input = [new ChatMessage(ChatRole.User, "soru")],
        Output = "cevap",
    };

    private static (ModelRunJudge Judge, InMemoryRunStore Runs) Build(FakeChatClient chatClient)
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));

        var options = new ModelRunJudgeOptions
        {
            Model = new ModelBinding { Provider = "test", Model = "cheap-model" },
        };
        options.Criteria.Add("Yanit dogru mu?");

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
    }
}
