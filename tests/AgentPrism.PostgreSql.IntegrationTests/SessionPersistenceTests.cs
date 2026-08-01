using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.PostgreSql.IntegrationTests;

/// <summary>
/// Oturum kaliciliginin uctan uca calistigini dogrular.
/// </summary>
/// <remarks>
/// Faz 1'den devreden iki acik is bu testlerle kapanir:
/// <see cref="ISessionStore"/> artik kullanilir ve <c>RunRecordingAgent</c>
/// calistirma kaydina gercek oturum kimligini yazar.
/// </remarks>
public sealed class SessionPersistenceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Oturum_kaydedilir_ve_yeni_bir_surecte_geri_yuklenir()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);

        const string SessionId = "musteri-42";

        // Birinci "surec": oturumu ac, calistir, kaydet.
        await using (var first = BuildProvider(context))
        {
            var agent = await ResolveAsync(first, "destek");
            var manager = first.GetRequiredService<AgentSessionManager>();

            var session = await manager.GetOrCreateSessionAsync(agent, SessionId);
            await agent.RunAsync("siparisim nerede", session);
            await manager.SaveSessionAsync(agent, session);
        }

        // Ikinci "surec": ayni kimlikle devam et.
        await using (var second = BuildProvider(context))
        {
            var agent = await ResolveAsync(second, "destek");
            var manager = second.GetRequiredService<AgentSessionManager>();

            var restored = await manager.GetOrCreateSessionAsync(agent, SessionId);

            AgentSessionIdentity.GetId(restored).ShouldBe(SessionId);

            var stored = await second.GetRequiredService<ISessionStore>().GetAsync(SessionId);
            stored.ShouldNotBeNull();
            stored.AgentName.ShouldBe("destek");
        }
    }

    [Fact]
    public async Task Sohbet_gecmisi_oturumlar_arasi_surer()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);

        const string SessionId = "gecmis-oturumu";

        await using (var first = BuildProvider(context))
        {
            var agent = await ResolveAsync(first, "destek");
            var manager = first.GetRequiredService<AgentSessionManager>();

            var session = await manager.GetOrCreateSessionAsync(agent, SessionId);
            await agent.RunAsync("birinci soru", session);
            await manager.SaveSessionAsync(agent, session);
        }

        await using (var second = BuildProvider(context))
        {
            var agent = await ResolveAsync(second, "destek");
            var manager = second.GetRequiredService<AgentSessionManager>();
            var provider = second.GetRequiredService<EchoModelProvider>();

            var session = await manager.GetOrCreateSessionAsync(agent, SessionId);
            await agent.RunAsync("ikinci soru", session);

            // Model, ilk turun mesajlarini da gormelidir; gecmis veritabanindan geldi.
            var texts = provider.LastRequest.Select(static message => message.Text).ToList();

            texts.ShouldContain(static text => text.Contains("birinci soru", StringComparison.Ordinal));
            texts.ShouldContain(static text => text.Contains("ikinci soru", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Calistirma_kaydi_gercek_oturum_kimligini_tasir()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);

        const string SessionId = "kayitli-oturum";

        await using var provider = BuildProvider(context);

        var agent = await ResolveAsync(provider, "destek");
        var manager = provider.GetRequiredService<AgentSessionManager>();

        var session = await manager.GetOrCreateSessionAsync(agent, SessionId);
        await agent.RunAsync("merhaba", session);

        var runs = await provider.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery());

        var run = runs.ShouldHaveSingleItem();
        run.SessionId.ShouldBe(SessionId);
        run.AgentName.ShouldBe("destek");
        run.Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task Oturumsuz_calistirmada_kayit_bos_oturum_kimligi_tasir()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);
        await using var provider = BuildProvider(context);

        var agent = await ResolveAsync(provider, "destek");
        await agent.RunAsync("oturumsuz");

        var runs = await provider.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery());

        runs.ShouldHaveSingleItem().SessionId.ShouldBeNull();
    }

    [Fact]
    public async Task Kimliksiz_oturum_kaydedilemez()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);
        await using var provider = BuildProvider(context);

        var agent = await ResolveAsync(provider, "destek");
        var manager = provider.GetRequiredService<AgentSessionManager>();

        // AgentSessionManager disinda acilmis bir oturumun AgentPrism kimligi yoktur.
        var session = await agent.CreateSessionAsync();

        await Should.ThrowAsync<AgentPrismException>(async () => await manager.SaveSessionAsync(agent, session));
    }

    [Fact]
    public async Task Oturum_silinince_geri_yukleme_yeni_oturum_acar()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);
        await using var provider = BuildProvider(context);

        var agent = await ResolveAsync(provider, "destek");
        var manager = provider.GetRequiredService<AgentSessionManager>();

        var session = await manager.GetOrCreateSessionAsync(agent, "gecici");
        await manager.SaveSessionAsync(agent, session);

        (await manager.DeleteSessionAsync("gecici")).ShouldBeTrue();
        (await manager.DeleteSessionAsync("gecici")).ShouldBeFalse();

        var fresh = await manager.GetOrCreateSessionAsync(agent, "gecici");
        AgentSessionIdentity.GetId(fresh).ShouldBe("gecici");
    }

    private static async ValueTask<Microsoft.Agents.AI.AIAgent> ResolveAsync(IServiceProvider provider, string name)
        => await provider.GetRequiredService<IAgentCatalog>().ResolveAsync(name)
           ?? throw new InvalidOperationException($"'{name}' agent'i cozulemedi.");

    private ServiceProvider BuildProvider(PostgresTestContext context)
    {
        var services = new ServiceCollection();

        services.AddSingleton(context.TenantContext);
        services.AddSingleton<EchoModelProvider>();

        services.AddAgentPrism()
            .AddModelProvider(static provider => provider.GetRequiredService<EchoModelProvider>())
            .AddAgent(new AgentDefinition
            {
                Name = "destek",
                Instructions = "Kisa yanit ver.",
                Model = new ModelBinding { Provider = "echo", Model = "echo-1" },
            })
            .UsePostgreSql(options =>
            {
                options.ConnectionString = fixture.ConnectionString;
                options.SchemaName = context.SchemaName;
                options.AutoApplyMigrations = false;
            });

        return services.BuildServiceProvider();
    }
}
