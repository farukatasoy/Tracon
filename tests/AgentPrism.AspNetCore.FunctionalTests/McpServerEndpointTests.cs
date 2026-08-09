using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Faz 50: AgentPrism agent'larini disa acan MCP sunucusu.
/// </summary>
public sealed class McpServerEndpointTests
{
    [Fact]
    public async Task Bos_beyaz_liste_hicbir_tool_dondurmez()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(),
            configureAfterMap: app => app.MapAgentPrismMcpServer());

        var (response, body) = await McpTestClient.SendAsync(host.Client, "/agentprism/mcp", "tools/list");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Tools(body!.Value).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Beyaz_listedeki_agent_tool_olarak_gorunur()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(o => o.ExposedAgents.Add("kod-agent")),
            configureAfterMap: app => app.MapAgentPrismMcpServer());

        var (_, body) = await McpTestClient.SendAsync(host.Client, "/agentprism/mcp", "tools/list");

        var tools = Tools(body!.Value);
        tools.GetArrayLength().ShouldBe(1);
        tools[0].GetProperty("name").GetString().ShouldBe("agentprism_kod-agent");
    }

    [Fact]
    public async Task ToolsCall_agenti_calistirir_ve_runs_satiri_uretir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(o => o.ExposedAgents.Add("kod-agent")),
            configureAfterMap: app => app.MapAgentPrismMcpServer());

        var (response, body) = await McpTestClient.SendAsync(
            host.Client,
            "/agentprism/mcp",
            "tools/call",
            new { name = "agentprism_kod-agent", arguments = new { message = "merhaba" } });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var result = body!.Value.GetProperty("result");

        // basarili yanitta IsError null'dir ve JSON'a hic yazilmaz.
        (!result.TryGetProperty("isError", out var isError) || !isError.GetBoolean()).ShouldBeTrue();

        var text = result.GetProperty("content")[0].GetProperty("text").GetString().ShouldNotBeNull();
        text.ShouldContain("merhaba", Case.Sensitive);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var all = await runs.QueryRunsAsync(new RunQuery());

        all.ShouldHaveSingleItem().AgentName.ShouldBe("kod-agent");
    }

    [Fact]
    public async Task Bilinmeyen_tool_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(),
            configureAfterMap: app => app.MapAgentPrismMcpServer());

        var (_, body) = await McpTestClient.SendAsync(
            host.Client,
            "/agentprism/mcp",
            "tools/call",
            new { name = "agentprism_kod-agent", arguments = new { message = "merhaba" } });

        var result = body!.Value.GetProperty("result");
        result.GetProperty("isError").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Onay_gerektiren_tool_tasiyan_agent_disa_acilamaz()
    {
        // Guard artik Map* aninda senkron degil, McpApprovalGuardFilter icinde
        // arka planda calisir (bkz. AgentPrismMcpServerExtensions) — hata bu
        // yuzden MapAgentPrismMcpServer()'dan degil, ILK istekten firlar.
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddTool(
                    (Func<string, string>)CancelOrder,
                    name: "cancel_order",
                    description: "Bir siparisi iptal eder.",
                    requiresApproval: true)
                .AddAgent(TestData.Definition() with { ToolNames = ["cancel_order"] })
                .UseMcpServer(o => o.ExposedAgents.Add("kod-agent")),
            configureAfterMap: app => app.MapAgentPrismMcpServer());

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => McpTestClient.SendAsync(host.Client, "/agentprism/mcp", "tools/list"));

        exception.Message.ShouldContain("onay", Case.Sensitive);
    }

    [Fact]
    public async Task Bos_sqlite_veritabaninda_MapAgentPrismMcpServer_cokmez()
    {
        // Regresyon: MapAgentPrismMcpServer() onceden Map* aninda (migration'lar
        // baslamadan ONCE) katalogu SENKRON okuyordu; tamamen bos (dosyasi HENUZ
        // olusmamis) bir veritabaninda "no such table" ile cokerdi. Guard
        // McpApprovalGuardFilter'a tasindiktan sonra Map* artik DB'ye hic
        // dokunmuyor. `:memory:` KULLANILMAZ: paylasilan onbellek olmadan her
        // yeni baglanti kendi izole bos veritabanini acar, gercek "bos dosya"
        // durumunu taklit etmez.
        var databasePath = Path.Combine(Path.GetTempPath(), $"agentprism-mcp-empty-db-{Guid.NewGuid():N}.db");

        try
        {
            await using var host = await AgentPrismTestHost.StartAsync(
                configureAgentPrism: builder => builder
                    .UseSqlite($"Data Source={databasePath}")
                    .AddAgent(TestData.Definition())
                    .UseMcpServer(o => o.ExposedAgents.Add("kod-agent")),
                configureAfterMap: app => app.MapAgentPrismMcpServer());

            var (response, body) = await McpTestClient.SendAsync(host.Client, "/agentprism/mcp", "tools/list");

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            Tools(body!.Value).GetArrayLength().ShouldBe(1);
        }
        finally
        {
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm", ".agentprism-migration-lock" })
            {
                File.Delete(databasePath + suffix);
            }
        }
    }

    [Fact]
    public async Task Derinlik_siniri_alt_cagriyi_engeller()
    {
        const string RouterModel = "router-model";
        const string ResearcherModel = "researcher-model";
        const string StartTask = "background_agents_start_task";
        const string WaitForCompletion = "background_agents_wait_for_first_completion";
        const string GetResults = "background_agents_get_task_results";

        await using var host = await AgentPrismTestHost.StartAsync(configureAgentPrism: builder =>
        {
            var provider = new AgentPrism.Testing.FakeModelProvider("routing")
                .ForModel(RouterModel, cfg => cfg
                    .CallsTool(StartTask, new { agentName = "arastirmaci", input = "alt gorev", description = "alt gorev" })
                    .CallsTool(WaitForCompletion, new { taskIds = new[] { 1 } })
                    .CallsTool(GetResults, new { taskId = 1 })
                    .EchoesLastToolResult("Devredildi: ", inputTokens: 4, outputTokens: 6))
                .ForModel(ResearcherModel, cfg => cfg.RespondsWith("Alt gorev tamam", inputTokens: 4, outputTokens: 6));

            builder.AddModelProvider(provider);

            builder.AddAgent(new AgentDefinition
            {
                Name = "arastirmaci",
                Description = "Arastirma yapar.",
                Instructions = "Arastir.",
                Model = new ModelBinding { Provider = "routing", Model = ResearcherModel },
                Origin = AgentDefinitionOrigin.Code,
            });

            builder.AddAgent(new AgentDefinition
            {
                Name = "yonlendirici",
                Description = "Isi devreder.",
                Instructions = "Devret.",
                Model = new ModelBinding { Provider = "routing", Model = RouterModel },
                CallableAgentNames = ["arastirmaci"],
                Origin = AgentDefinitionOrigin.Code,
            });

            // MaxDepth=0: dis cagrinin KENDISI kok (Depth 0); bir tek alt
            // cagri bile Depth 1 uretir ve 0 sinirini asar. Boylece "disaridan
            // cagrilan agent alt agent cagiramaz" acikca dogrulanir.
            builder.UseMcpServer(o =>
            {
                o.ExposedAgents.Add("yonlendirici");
                o.Budget = new AgentRunBudget { MaxDepth = 0 };
            });
        },
        configureAfterMap: app => app.MapAgentPrismMcpServer());

        var (_, body) = await McpTestClient.SendAsync(
            host.Client,
            "/agentprism/mcp",
            "tools/call",
            new { name = "agentprism_yonlendirici", arguments = new { message = "baslat" } });

        var text = body!.Value.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString().ShouldNotBeNull();
        text.ShouldContain("cagri derinligi siniri asildi", Case.Sensitive);

        var runs = host.Services.GetRequiredService<IRunStore>();
        (await runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task AllowRemoteAccess_acikken_MCP_acilmaz()
    {
        await Should.ThrowAsync<InvalidOperationException>(() => AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(o => o.ExposedAgents.Add("kod-agent")),
            configureEndpoints: o => o.AllowRemoteAccess = true,
            configureAfterMap: app => app.MapAgentPrismMcpServer()));
    }

    [Fact]
    public async Task Kimliksiz_istek_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(o => o.ExposedAgents.Add("kod-agent")),
            configureEndpoints: o => o.AuthToken = "s3cr3t",
            configureAfterMap: app => app.MapAgentPrismMcpServer());

        var (response, _) = await McpTestClient.SendAsync(host.Client, "/agentprism/mcp", "tools/list");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dogru_token_ile_istek_kabul_edilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(o => o.ExposedAgents.Add("kod-agent")),
            configureEndpoints: o => o.AuthToken = "s3cr3t",
            configureAfterMap: app => app.MapAgentPrismMcpServer());

        var (response, _) = await McpTestClient.SendAsync(host.Client, "/agentprism/mcp", "tools/list", token: "s3cr3t");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Cagri_denetim_izine_yazilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(o => o.ExposedAgents.Add("kod-agent")),
            configureAfterMap: app => app.MapAgentPrismMcpServer());

        await McpTestClient.SendAsync(
            host.Client,
            "/agentprism/mcp",
            "tools/call",
            new { name = "agentprism_kod-agent", arguments = new { message = "merhaba" } });

        var auditLog = host.Services.GetRequiredService<IAuditLog>();
        var entries = await auditLog.QueryAsync(new AuditQuery { Action = "external.call" });

        var entry = entries.ShouldHaveSingleItem();
        entry.Entity.ShouldBe("agent:kod-agent");
        entry.After.ShouldNotBeNull().ShouldContain("\"protocol\":\"mcp\"", Case.Sensitive);
    }

    [Fact]
    public async Task Dinamik_katalog_yeni_agent_sunucu_yeniden_kurulmadan_gorunur()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder.UseMcpServer(o => o.ExposedAgents.Add("db-agent")),
            configureAfterMap: app => app.MapAgentPrismMcpServer());

        var before = Tools((await McpTestClient.SendAsync(host.Client, "/agentprism/mcp", "tools/list")).Body!.Value);
        before.GetArrayLength().ShouldBe(0);

        using var createResponse = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/agents", UriKind.Relative),
            TestData.Request());

        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var after = Tools((await McpTestClient.SendAsync(host.Client, "/agentprism/mcp", "tools/list")).Body!.Value);
        after.GetArrayLength().ShouldBe(1);
        after[0].GetProperty("name").GetString().ShouldBe("agentprism_db-agent");
    }

    [Fact]
    public async Task Kiraci_yalitimi_korunur()
    {
        const string TenantHeader = "X-AgentPrism-Tenant";

        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .UseTenancy(o =>
                {
                    o.Enabled = true;
                    o.AllowHeaderResolution = true;
                })
                .UseMcpServer(o => o.ExposeAllAgents = true),
            configureAfterMap: app => app.MapAgentPrismMcpServer());

        using var createRequest = new HttpRequestMessage(
            HttpMethod.Post, new Uri("/agentprism/api/agents", UriKind.Relative))
        {
            Content = JsonContent.Create(TestData.Request()),
        };
        createRequest.Headers.Add(TenantHeader, "kiraci-a");

        using var createResponse = await host.Client.SendAsync(createRequest);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Kiraci a kendi agent'ini gorur.
        var mine = Tools((await McpTestClient.SendAsync(
            host.Client, "/agentprism/mcp", "tools/list", tenantHeader: "kiraci-a")).Body!.Value);
        mine.GetArrayLength().ShouldBe(1);

        // Kiraci b hicbir sey gormez — kiraci basligi kimlik kaniti degildir
        // ama HttpTenantContext'in kendisi zaten baska bir kiracinin verisini
        // baska bir kiraciya gostermez (TenancyTests ile ayni sinir).
        var theirs = Tools((await McpTestClient.SendAsync(
            host.Client, "/agentprism/mcp", "tools/list", tenantHeader: "kiraci-b")).Body!.Value);
        theirs.GetArrayLength().ShouldBe(0);
    }

    private static string CancelOrder(string orderId) => $"iptal edildi: {orderId}";

    private static System.Text.Json.JsonElement Tools(System.Text.Json.JsonElement body)
        => body.GetProperty("result").GetProperty("tools");
}
