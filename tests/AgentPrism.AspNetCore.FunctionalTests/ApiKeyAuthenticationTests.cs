using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// API anahtarinin ikinci bir kimlik kaynagi olarak dogrulanmasi, kiraci
/// cozumlemesi ve kapsam denetimi (Faz 53).
/// </summary>
/// <remarks>
/// 🚨 Bu testlerin korudugu asil kural: kiraci basliktan degil anahtardan
/// cozulur ve baslik anahtarin kiracisini EZEMEZ (bolum 53.5).
/// </remarks>
public sealed class ApiKeyAuthenticationTests
{
    private const string TenantHeader = "X-AgentPrism-Tenant";

    // --- Temel dogrulama ---

    [Fact]
    public async Task Gecerli_anahtarla_istek_baslik_olmadan_gecer()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "ci", "AgentsRead");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Statik_token_tanimsizken_bilinmeyen_deger_401_alir()
    {
        // 🚨 Kasitli davranis degisikligi (K-XXX): AuthToken tanimsizken bir
        // baslik hic gonderilmezse eski davranis (acik erisim) korunur, ama
        // bir Authorization basligi GONDERILIRSE artik statik veya API
        // anahtariyla dogrulanmalidir.
        await using var host = await AgentPrismTestHost.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "boyle-bir-anahtar-yok");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Baslik_yokken_ve_hicbir_token_tanimliyken_eski_davranis_korunur()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/agents", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Iptal_edilen_anahtar_401_alir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "ci", "AgentsRead");
        (await host.Client.DeleteAsync(
            new Uri($"/agentprism/api/api-keys/{created.Record.Id}", UriKind.Relative))).Dispose();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Suresi_gecmis_anahtar_401_alir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var store = host.Services.GetRequiredService<IApiKeyStore>();
        var created = await store.CreateAsync(new ApiKeyDraft
        {
            TenantId = "default",
            Name = "ci",
            Scopes = [ApiKeyScope.AgentsRead],
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // --- Kiraci cozumlemesi (bolum 53.5) ---

    [Fact]
    public async Task Kiraci_basliktan_degil_anahtardan_cozulur()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        var store = host.Services.GetRequiredService<IApiKeyStore>();
        var created = await store.CreateAsync(new ApiKeyDraft
        {
            TenantId = "kiraci-a",
            Name = "ci",
            Scopes = [ApiKeyScope.AgentsRead],
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/tenants/current");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var current = await response.Content.ReadFromJsonAsync<CurrentTenantResponse>();
        current.ShouldNotBeNull().TenantId.ShouldBe("kiraci-a");
    }

    [Fact]
    public async Task Baslik_anahtarin_kiracisiyla_eslesirse_gecer()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        var store = host.Services.GetRequiredService<IApiKeyStore>();
        var created = await store.CreateAsync(new ApiKeyDraft
        {
            TenantId = "kiraci-a",
            Name = "ci",
            Scopes = [ApiKeyScope.AgentsRead],
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);
        request.Headers.Add(TenantHeader, "kiraci-a");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Baslik_anahtarin_kiracisindan_farkliysa_403_alir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        var store = host.Services.GetRequiredService<IApiKeyStore>();
        var created = await store.CreateAsync(new ApiKeyDraft
        {
            TenantId = "kiraci-a",
            Name = "ci",
            Scopes = [ApiKeyScope.AgentsRead],
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);
        request.Headers.Add(TenantHeader, "kiraci-b");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- Kapsam denetimi (bolum 53.3) ---

    [Fact]
    public async Task Yetersiz_kapsamli_anahtar_403_alir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "readonly", "RunsRead");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/agentprism/api/agents/kod-agent/run")
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "merhaba" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Dogru_kapsamli_anahtar_calistirmayi_baslatir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "runner", "RunsWrite");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/agentprism/api/agents/kod-agent/run")
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "merhaba" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Kapsamsiz_ucta_denetim_yoktur()
    {
        // /api/tenants/current korumali grubun ARKASINDAKI TEK kapsamsiz uctur
        // (§7.2, Aile F) — herhangi bir kapsamli anahtarla erisilebilmelidir.
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "reader", "AgentsRead");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/tenants/current");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Kapsamli_ucta_yanlis_kapsam_403_doner()
    {
        // /api/api-keys artik SecurityAdmin gerektirir (Aile F); AgentsRead
        // kapsamli bir anahtar erisemez.
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "reader", "AgentsRead");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/api-keys");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- Dis yuzey kilidi (bolum 53.4) ---

    [Fact]
    public async Task AllowRemoteAccess_acikken_external_invoke_anahtari_yoksa_MCP_acilamaz()
    {
        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await using var host = await AgentPrismTestHost.StartAsync(
                configureAgentPrism: static builder => builder
                    .AddAgent(TestData.Definition())
                    .UseMcpServer(),
                configureEndpoints: static options => options.AllowRemoteAccess = true,
                configureAfterMap: static app => app.MapAgentPrismMcpServer());
        });
    }

    [Fact]
    public async Task AllowRemoteAccess_acikken_external_invoke_anahtariyla_MCP_acilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: static builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(static options => options.ExposedAgents.Add("kod-agent")),
            configureEndpoints: static options => options.AllowRemoteAccess = true,
            configureAfterMap: static app =>
            {
                var store = app.Services.GetRequiredService<IApiKeyStore>();

                store.CreateAsync(new ApiKeyDraft
                {
                    TenantId = "default",
                    Name = "mcp",
                    Scopes = [ApiKeyScope.ExternalInvoke],
                }).AsTask().GetAwaiter().GetResult();

                app.MapAgentPrismMcpServer();
            });

        var (response, _) = await McpTestClient.SendAsync(host.Client, "/agentprism/mcp", "tools/list");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
