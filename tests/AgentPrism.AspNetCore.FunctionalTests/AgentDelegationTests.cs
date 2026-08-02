using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Bir agent'in baska bir agent'i gercekten cagirdigi uctan uca senaryo.
/// </summary>
/// <remarks>
/// Senaryo iki agent'la kurulur: <c>yonlendirici</c> isi <c>arastirmaci</c>'ya
/// devreder. Model saglayicisi aga cikmaz ama Microsoft Agent Framework'un
/// gercek arka plan gorev tool'larini cagirir; sahte bir kisayol kullanilmaz.
/// </remarks>
public sealed class AgentDelegationTests
{
    [Fact]
    public async Task Akissiz_cagri_iki_ayri_calistirma_satiri_uretir()
    {
        await using var host = await StartAsync();

        var agent = await ResolveRouterAsync(host);
        var response = await agent.RunAsync("baslat");

        response.Text.ShouldContain("Devredildi", Case.Sensitive);

        await AssertTreeAsync(host);
    }

    [Fact]
    public async Task Akisli_cagri_da_iki_ayri_calistirma_satiri_uretir()
    {
        // 🚨 Akisli yol ayrica test edilir. Kapsam bir AsyncLocal'de yasar ve bir
        // async iterator govdesinde yapilan atama `yield return` sinirini asmaz;
        // yalnizca akissiz yolu test etmek bu regresyonu kacirirdi.
        await using var host = await StartAsync();

        var agent = await ResolveRouterAsync(host);

        await foreach (var update in agent.RunStreamingAsync("baslat"))
        {
            _ = update;
        }

        await AssertTreeAsync(host);
    }

    [Fact]
    public async Task Kok_calistirmaya_alt_calistirma_ozet_olaylari_yazilir()
    {
        await using var host = await StartAsync();

        var agent = await ResolveRouterAsync(host);
        await agent.RunAsync("baslat");

        var runs = host.Services.GetRequiredService<IRunStore>();
        var root = (await runs.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        var types = new List<RunEventType>();

        await foreach (var runEvent in runs.ReadEventsAsync(root.Id))
        {
            types.Add(runEvent.Type);
        }

        // Alt calistirmanin TAM akisi aynalanmaz; yalnizca basladigi ve bittigi
        // bildirilir. Aynalama olay hacmini agac boyunca katlardi.
        types.ShouldContain(RunEventType.ChildRunStarted);
        types.ShouldContain(RunEventType.ChildRunCompleted);
        types.Count(static type => type == RunEventType.RunStarted).ShouldBe(1);
    }

    [Fact]
    public async Task Derinlik_siniri_sifirsa_alt_cagri_yapilamaz()
    {
        await using var host = await StartAsync(maxDepth: 0);

        var agent = await ResolveRouterAsync(host);
        var response = await agent.RunAsync("baslat");

        response.Text.ShouldContain("cagri derinligi siniri asildi", Case.Sensitive);

        var runs = host.Services.GetRequiredService<IRunStore>();

        // Alt calistirma HIC baslamaz; reddedilen cagri satir uretmez.
        (await runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).ShouldHaveSingleItem();
    }

    private static async Task<Microsoft.Agents.AI.AIAgent> ResolveRouterAsync(AgentPrismTestHost host)
    {
        var catalog = host.Services.GetRequiredService<IAgentCatalog>();

        return (await catalog.ResolveAsync("yonlendirici")).ShouldNotBeNull();
    }

    private static async Task AssertTreeAsync(AgentPrismTestHost host)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();
        var all = await runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false });

        all.Count.ShouldBe(2);

        var root = all.Single(static run => run.Depth == 0);
        var child = all.Single(static run => run.Depth == 1);

        root.AgentName.ShouldBe("yonlendirici");
        root.ParentRunId.ShouldBeNull();
        root.ChildRunCount.ShouldBe(1);

        child.AgentName.ShouldBe("arastirmaci");
        child.ParentRunId.ShouldBe(root.Id);
        child.RootRunId.ShouldBe(root.Id);
        child.Status.ShouldBe(RunStatus.Completed);

        // Agac toplami her iki calistirmanin tokenlerini birlestirir.
        root.TreeUsage!.TotalTokens.ShouldBe(20);
        root.Usage!.TotalTokens.ShouldBe(10);
    }

    private static Task<AgentPrismTestHost> StartAsync(int maxDepth = 3)
        => AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder =>
            {
                builder.AddModelProvider(new RoutingModelProvider());

                builder.AddAgent(new AgentDefinition
                {
                    Name = "arastirmaci",
                    Description = "Arastirma yapar.",
                    Instructions = "Arastir.",
                    Model = Binding(),
                    Origin = AgentDefinitionOrigin.Code,
                });

                builder.AddAgent(new AgentDefinition
                {
                    Name = "yonlendirici",
                    Description = "Isi devreder.",
                    Instructions = "Devret.",
                    Model = Binding(),
                    CallableAgentNames = ["arastirmaci"],
                    Origin = AgentDefinitionOrigin.Code,
                });
            },
            configureServices: services => services.Configure<AgentPrismOptions>(
                options => options.AgentGraph.MaxDepth = maxDepth));

    private static ModelBinding Binding()
        => new() { Provider = RoutingModelProvider.ProviderName, Model = RoutingModelProvider.ModelName };
}
