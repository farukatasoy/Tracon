using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Faz 6 uclari: telemetri, tool kullanimi, kiracilar, MCP sunuculari ve onay kurallari.
/// </summary>
public sealed class GovernanceEndpointTests
{
    [Fact]
    public async Task Trace_yoksa_404_ve_gerekce_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(
            new Uri($"/agentprism/api/runs/{Guid.NewGuid()}/trace", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // 404 burada bir hata degil, ORNEKLEME sonucudur; gerekce yanitta yazili
        // olmali ki operator bosuna hata aramasin.
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldContain("SuccessSampleRatio");
    }

    [Fact]
    public async Task Tool_kullanimi_bos_baslar()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var usage = await host.Client.GetFromJsonAsync<List<ToolUsage>>(
            new Uri("/agentprism/api/tools/usage", UriKind.Relative));

        usage.ShouldNotBeNull();
        usage.ShouldBeEmpty();
    }

    [Fact]
    public async Task Calistirma_tool_cagrilari_listelenir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var runs = host.Services.GetRequiredService<IRunStore>();
        var runId = AgentPrismId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "support",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = "default",
        });

        await runs.RecordToolInvocationAsync(new ToolInvocationRecord
        {
            Id = AgentPrismId.NewId(),
            RunId = runId,
            ToolName = "get_order_status",
            Duration = TimeSpan.FromMilliseconds(42),
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var invocations = await host.Client.GetFromJsonAsync<List<ToolInvocationRecord>>(
            new Uri($"/agentprism/api/runs/{runId}/tools", UriKind.Relative));

        invocations.ShouldNotBeNull();
        invocations.ShouldHaveSingleItem().ToolName.ShouldBe("get_order_status");
    }

    [Fact]
    public async Task Gecerli_kiraci_donduruluyor()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var current = await host.Client.GetFromJsonAsync<CurrentTenantResponse>(
            new Uri("/agentprism/api/tenants/current", UriKind.Relative));

        current.ShouldNotBeNull();
        current.TenantId.ShouldBe("default");
    }

    [Fact]
    public async Task Kiraci_kaydi_yazilir_ve_silinir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var saved = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/tenants/acme", UriKind.Relative),
            new { displayName = "Acme A.S." });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK);

        var tenants = await host.Client.GetFromJsonAsync<List<TenantDescriptor>>(
            new Uri("/agentprism/api/tenants", UriKind.Relative));

        tenants.ShouldNotBeNull();
        tenants.ShouldHaveSingleItem().Slug.ShouldBe("acme");

        using var deleted = await host.Client.DeleteAsync(
            new Uri("/agentprism/api/tenants/acme", UriKind.Relative));

        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Gecersiz_kiraci_anahtari_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/tenants/bosluk%20var", UriKind.Relative),
            new { displayName = "Gecersiz" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Mcp_sunucusu_yazilir_ve_listelenir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var saved = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/mcp-servers/github", UriKind.Relative),
            new
            {
                endpoint = "https://mcp.example.com/mcp",
                transport = "StreamableHttp",
                authorizationConfigurationKey = "AgentPrism:Mcp:GithubToken",
                enabled = true,
                requiresApproval = true,
            });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK);

        var servers = await host.Client.GetFromJsonAsync<List<McpServerDefinition>>(
            new Uri("/agentprism/api/mcp-servers", UriKind.Relative));

        servers.ShouldNotBeNull();

        var server = servers.ShouldHaveSingleItem();

        server.Name.ShouldBe("github");
        server.RequiresApproval.ShouldBeTrue();
    }

    [Fact]
    public async Task Mcp_yaniti_sir_tasimaz()
    {
        // Sunucu kaydi kimlik dogrulama DEGERINI hicbir zaman tasimaz; yalnizca
        // degerin okunacagi yapilandirma anahtarinin ADINI tasir (karar K-059).
        //
        // Test bunu, sozlesmede OLMAYAN bir 'authorization' alani gondererek
        // dogrular: alan baglanmaz, saklanmaz ve hicbir yanitta geri donmez.
        const string Sizdirilmaya_Calisilan = "COK-GIZLI-DEGER-TESTI";

        await using var host = await AgentPrismTestHost.StartAsync();

        using var saved = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/mcp-servers/github", UriKind.Relative),
            new
            {
                endpoint = "https://mcp.example.com/mcp",
                authorizationConfigurationKey = "AgentPrism:Mcp:GithubToken",
                authorization = Sizdirilmaya_Calisilan,
                headers = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["X-Sunucu"] = "ornek",
                },
                enabled = true,
                requiresApproval = true,
            });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await saved.Content.ReadAsStringAsync()).ShouldNotContain(Sizdirilmaya_Calisilan);

        using var listed = await host.Client.GetAsync(
            new Uri("/agentprism/api/mcp-servers", UriKind.Relative));

        (await listed.Content.ReadAsStringAsync()).ShouldNotContain(Sizdirilmaya_Calisilan);
    }

    [Fact]
    public async Task Stdio_adresi_reddedilir()
    {
        // Yerel surec aktarimi bir guvenlik sinirdir: arayuze erisen biri
        // sunucuda program calistiramamalidir.
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/mcp-servers/yerel", UriKind.Relative),
            new { endpoint = "file:///usr/local/bin/mcp-server", enabled = true, requiresApproval = true });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await response.Content.ReadAsStringAsync()).ShouldContain("stdio");
    }

    [Fact]
    public async Task Gecersiz_adres_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/mcp-servers/bozuk", UriKind.Relative),
            new { endpoint = "bu-bir-adres-degil", enabled = true, requiresApproval = true });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Mcp_kayitli_degilse_tazeleme_501_doner()
    {
        // AgentPrism.Mcp istege bagli bir pakettir; kayitli degilse uc acikca
        // "uygulanmadi" der ve sessizce basarili gorunmez.
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsync(
            new Uri("/agentprism/api/mcp-servers/refresh", UriKind.Relative),
            content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task Mcp_sunucusu_oauth_alanlariyla_yazilir_ve_listelenir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var saved = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/mcp-servers/github", UriKind.Relative),
            new
            {
                endpoint = "https://mcp.example.com/mcp",
                transport = "StreamableHttp",
                enabled = true,
                requiresApproval = true,
                oauthEnabled = true,
                oauthClientId = "agentprism-client",
                oauthClientSecretConfigurationKey = "AgentPrism:Mcp:GithubClientSecret",
                oauthScopes = "repo read:user",
            });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK);

        var servers = await host.Client.GetFromJsonAsync<List<McpServerDefinition>>(
            new Uri("/agentprism/api/mcp-servers", UriKind.Relative));

        var server = servers.ShouldHaveSingleItem();

        server.OAuthEnabled.ShouldBeTrue();
        server.OAuthClientId.ShouldBe("agentprism-client");
        server.OAuthClientSecretConfigurationKey.ShouldBe("AgentPrism:Mcp:GithubClientSecret");
        server.OAuthScopes.ShouldBe("repo read:user");
        server.OAuthAuthorizationMode.ShouldBe(McpOAuthAuthorizationMode.AuthorizationCode);
    }

    [Fact]
    public async Task Oauth_istemci_kimligi_eksikse_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/mcp-servers/github", UriKind.Relative),
            new { endpoint = "https://mcp.example.com/mcp", enabled = true, oauthEnabled = true });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Oauth_ile_statik_yetkilendirme_baslikcakisirsa_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/mcp-servers/github", UriKind.Relative),
            new
            {
                endpoint = "https://mcp.example.com/mcp",
                enabled = true,
                oauthEnabled = true,
                oauthClientId = "agentprism-client",
                authorizationConfigurationKey = "AgentPrism:Mcp:GithubToken",
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Mcp_kayitli_degilse_prompt_ve_kaynak_uclari_501_doner()
    {
        // Prompts/Resources/OAuth (Faz 22) da AgentPrism.Mcp'ye bagimlidir;
        // kayitli degilse ayni acik "uygulanmadi" davranisini sergilemeli.
        await using var host = await AgentPrismTestHost.StartAsync();

        using var prompts = await host.Client.GetAsync(
            new Uri("/agentprism/api/mcp-servers/github/prompts", UriKind.Relative));

        prompts.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);

        using var resources = await host.Client.GetAsync(
            new Uri("/agentprism/api/mcp-servers/github/resources", UriKind.Relative));

        resources.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);

        using var read = await host.Client.GetAsync(
            new Uri("/agentprism/api/mcp-servers/github/resources/read?uri=file:///a", UriKind.Relative));

        read.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);

        using var oauthStart = await host.Client.PostAsync(
            new Uri("/agentprism/api/mcp-servers/github/oauth/start", UriKind.Relative),
            content: null);

        oauthStart.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task Mcp_oauth_callback_bearer_token_olmadan_erisilir()
    {
        // Callback ucu erisim katmanlarinin disindadir: saglayicinin yonlendirdigi
        // tarayici bizim bearer token'imizi tasiyamaz. Bu istekte hicbir
        // Authorization basligi YOKTUR ve uc yine de 200 doner (basarisiz bir
        // HTML sayfasiyla, cunku bu test host'unda AgentPrism.Mcp kayitli degil).
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(
            new Uri("/agentprism/api/mcp-servers/github/oauth/callback?code=abc&state=bilinmeyen", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");

        var body = await response.Content.ReadAsStringAsync();

        body.ShouldContain("failed");
    }

    [Fact]
    public async Task Onay_kurallari_listelenir_ve_geri_alinir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var store = host.Services.GetRequiredService<IToolApprovalRuleStore>();

        var rule = await store.AddAsync(new ToolApprovalRule
        {
            Id = AgentPrismId.NewId(),
            TenantId = "default",
            AgentName = "support",
            ToolName = "cancel_order",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var rules = await host.Client.GetFromJsonAsync<List<ToolApprovalRule>>(
            new Uri("/agentprism/api/approvals/rules", UriKind.Relative));

        rules.ShouldNotBeNull();
        rules.ShouldHaveSingleItem().ToolName.ShouldBe("cancel_order");

        using var deleted = await host.Client.DeleteAsync(
            new Uri($"/agentprism/api/approvals/rules/{rule.Id}", UriKind.Relative));

        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Olmayan_kural_silinemez()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.DeleteAsync(
            new Uri($"/agentprism/api/approvals/rules/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Bos_calistirma_istegi_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(static builder => builder.AddAgent(TestData.Definition("echo")));

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/agents/echo/run", UriKind.Relative),
            new { });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await response.Content.ReadAsStringAsync()).ShouldContain("approvals");
    }

    [Fact]
    public async Task Oturumsuz_onay_istegi_reddedilir()
    {
        // Bekleyen onay istegi oturum gecmisinde yasar; oturumsuz bir istekte
        // eslesecek bir sey yoktur.
        await using var host = await AgentPrismTestHost.StartAsync(static builder => builder.AddAgent(TestData.Definition("echo")));

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/agents/echo/run", UriKind.Relative),
            new
            {
                approvals = new[] { new { requestId = "req-1", approved = true } },
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await response.Content.ReadAsStringAsync()).ShouldContain("sessionId");
    }
}
