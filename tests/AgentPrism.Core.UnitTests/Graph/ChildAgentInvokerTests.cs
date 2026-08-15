using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Graph;

/// <summary>
/// Alt agent sarmalayicisinin uyguladigi sinirlar: derinlik, butce, kiraci ve onay.
/// </summary>
/// <remarks>
/// Sarmalayici <c>options = null</c> ile cagrilir - Microsoft Agent Framework'un
/// arka plan gorev tool'u boyle cagirir (Faz 12'de olculdu). Testler bu gercek
/// cagri bicimini tekrarlar; ayarlari parametre olarak vermek gercek yolu
/// atlardi ve regresyonu kacirirdi.
/// </remarks>
public sealed class ChildAgentInvokerTests
{
    [Fact]
    public async Task Kapsam_yoksa_cagri_reddedilir()
    {
        var (invoker, _) = CreateInvoker();

        AgentPrismRunContext.SetCurrent(null);

        var response = await invoker.RunAsync("calis");

        response.Text.ShouldContain("calistirma kaydi kapali", Case.Sensitive);
    }

    [Fact]
    public async Task Derinlik_siniri_asilirsa_cagri_reddedilir()
    {
        var (invoker, store) = CreateInvoker();

        SetScope(depth: 3, budget: new AgentRunBudget { MaxDepth = 3 });

        var response = await invoker.RunAsync("calis");

        response.Text.ShouldContain("cagri derinligi siniri asildi", Case.Sensitive);

        // Ret bir istisna degildir ve alt calistirma HIC baslamaz.
        (await store.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).ShouldBeEmpty();
    }

    [Fact]
    public async Task Butce_bitince_yeni_alt_calistirma_baslamaz()
    {
        var (invoker, store) = CreateInvoker();
        var budget = new AgentRunBudget { MaxDepth = 3, MaxTotalRuns = 1 };

        SetScope(depth: 0, budget: budget);
        await invoker.RunAsync("birinci");

        SetScope(depth: 0, budget: budget);
        var second = await invoker.RunAsync("ikinci");

        second.Text.ShouldContain("child-run limit is reached", Case.Sensitive);
        (await store.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Kiraci_degistiyse_cagri_reddedilir()
    {
        // Alt cagri baska bir is parcaciginda calisir. Kiraci baglami kaybolursa
        // varsayilan kiraciya duser ve bir kiracinin agent'i baska bir kiracinin
        // verisiyle calisirdi. Sizinti tam burada olusur.
        var (invoker, store) = CreateInvoker();

        SetScope(depth: 0, budget: new AgentRunBudget { MaxDepth = 3 }, tenantId: "baska-kiraci");

        var response = await invoker.RunAsync("calis");

        response.Text.ShouldContain("kiracisindan cikamaz", Case.Sensitive);
        (await store.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).ShouldBeEmpty();
    }

    [Fact]
    public async Task Alt_calistirma_agaca_baglanir()
    {
        var (invoker, store) = CreateInvoker();
        var rootRunId = AgentPrismId.NewId();

        SetScope(depth: 0, budget: new AgentRunBudget { MaxDepth = 3 }, runId: rootRunId);

        await invoker.RunAsync("calis");

        var child = (await store.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).ShouldHaveSingleItem();
        child.ParentRunId.ShouldBe(rootRunId);
        child.RootRunId.ShouldBe(rootRunId);
        child.Depth.ShouldBe(1);
        child.AgentName.ShouldBe("arastirmaci");
    }

    [Fact]
    public async Task Ikinci_katman_kokunu_korur()
    {
        var (invoker, store) = CreateInvoker();
        var rootRunId = AgentPrismId.NewId();
        var middleRunId = AgentPrismId.NewId();

        // Ortadaki calistirma zaten agacin icindedir: kok kimligi ondan degil,
        // kapsamdan tasinir. Kopyalanan bir RootRunId burada bozulurdu.
        SetScope(depth: 1, budget: new AgentRunBudget { MaxDepth = 3 }, runId: middleRunId, rootRunId: rootRunId);

        await invoker.RunAsync("calis");

        var child = (await store.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).ShouldHaveSingleItem();
        child.ParentRunId.ShouldBe(middleRunId);
        child.RootRunId.ShouldBe(rootRunId);
        child.Depth.ShouldBe(2);
    }

    [Fact]
    public async Task Butce_agac_boyunca_TEK_ornektir()
    {
        var (invoker, _) = CreateInvoker();
        var budget = new AgentRunBudget { MaxDepth = 3, MaxTotalTokens = 1_000_000 };

        SetScope(depth: 0, budget: budget);

        await invoker.RunAsync("calis");

        // Alt calistirmanin harcamasi cagiranin gordugu ayni sayaca islenir.
        // Butce kopyalansaydi her dal kendi sifirindan baslar ve sinir anlamini
        // yitirirdi.
        budget.ConsumedTokens.ShouldBe(42);
        budget.StartedRuns.ShouldBe(1);
    }

    [Fact]
    public async Task Onay_isteyen_alt_calistirma_anlasilir_hata_verir()
    {
        var approvalTool = new ApprovalRequiredAIFunction(TestData.Tool("tehlikeli_is"));

        var client = new FakeChatClient(_ => new ChatResponse(
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "tehlikeli_is", null)])));

        var (invoker, store) = CreateInvoker(client, approvalTool);

        SetScope(depth: 0, budget: new AgentRunBudget { MaxDepth = 3 });

        var response = await invoker.RunAsync("calis");

        response.Text.ShouldContain("kullanici onayi istiyor", Case.Sensitive);
        response.Text.ShouldContain("otomatik onay kurali", Case.Sensitive);

        // Alt calistirma basarili sayilmaz: model bir sonucu degil,
        // cevaplanamayacak bir soruyu geri dondu.
        var child = (await store.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).ShouldHaveSingleItem();
        child.Status.ShouldBe(RunStatus.Failed);
        child.Error!.Message.ShouldContain("A child agent cannot request approval", Case.Sensitive);
    }

    /// <summary>Kapsami mevcut akisa yazar.</summary>
    /// <remarks>
    /// <see cref="AgentPrismRunContext"/> bir <c>AsyncLocal</c> uzerine kuruludur;
    /// atama testin kendi govdesinde yapilmalidir, bir yardimci <c>async</c>
    /// metotta degil. Bu metot bilerek es zamanlidir.
    /// </remarks>
    private static void SetScope(
        int depth,
        AgentRunBudget budget,
        Guid? runId = null,
        Guid? rootRunId = null,
        string tenantId = "test")
    {
        var id = runId ?? AgentPrismId.NewId();

        AgentPrismRunContext.SetCurrent(new AgentRunScope
        {
            RunId = id,
            RootRunId = rootRunId ?? id,
            Depth = depth,
            AgentName = "yonlendirici",
            TenantId = tenantId,
            Budget = budget,
        });
    }

    private static (ChildAgentInvoker Invoker, InMemoryRunStore Store) CreateInvoker(
        FakeChatClient? client = null,
        params AIFunction[] tools)
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var tenantContext = new FixedTenantContext();

        var chatClient = client ?? new FakeChatClient(_ => new ChatResponse(
            new ChatMessage(ChatRole.Assistant, "arastirma sonucu"))
        {
            Usage = new UsageDetails { TotalTokenCount = 42 },
        });

        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider(chatClient)),
            TestData.Registry(tools));

        var inner = new RunRecordingAgent(
            compiler.Compile(TestData.Definition(
                name: "arastirmaci",
                toolNames: [.. tools.Select(static tool => tool.Name)])),
            store,
            tenantContext,
            new AgentPrismRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance);

        var resolver = new CallableAgentResolver(new SingleAgentServices(inner));

        var invoker = new ChildAgentInvoker(
            resolver,
            tenantContext,
            NullLogger.Instance,
            "yonlendirici",
            new CallableAgentInfo("arastirmaci", "Arastirma yapar.", 1));

        return (invoker, store);
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public string TenantId => "test";
    }

    /// <summary>Tek bir agent'i dondüren en kucuk katalog.</summary>
    private sealed class SingleAgentServices(AIAgent agent) : IServiceProvider, IAgentCatalog
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(IAgentCatalog) ? this : null;

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<AgentDescriptor>)
            [
                new AgentDescriptor
                {
                    Name = "arastirmaci",
                    Origin = AgentDefinitionOrigin.Database,
                    SourceName = "database",
                },
            ]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, CancellationToken cancellationToken = default)
            => new(string.Equals(agentName, "arastirmaci", StringComparison.Ordinal) ? agent : null);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, int? version, CancellationToken cancellationToken = default)
            => ResolveAsync(agentName, cancellationToken);
    }
}
