using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Kiraci cozumleme ve yalitim.
/// </summary>
/// <remarks>
/// 🚨 Bu testlerin korudugu kural: <strong>baslik kimlik kaniti degildir</strong>.
/// Claim yapilandirilmissa baslik hic okunmamalidir; aksi halde kimlik
/// dogrulamasindan gecmis bir kullanici bir baslik ekleyerek baska bir kiracinin
/// verisine erisebilirdi.
/// </remarks>
public sealed class TenancyTests
{
    private const string TenantHeader = "X-AgentPrism-Tenant";

    [Fact]
    public async Task Kapaliyken_baslik_yok_sayilir()
    {
        // Varsayilan davranis: cok kiracililik kapali, her istek varsayilan
        // kiraciya duser. Tek kiracili kurulum hicbir ayar istemez.
        await using var host = await AgentPrismTestHost.StartAsync();

        (await ReadTenantAsync(host, header: "kiraci-b")).ShouldBe("default");
    }

    [Fact]
    public async Task Baslik_cozumu_acikca_acilmadikca_calismaz()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.UseTenancy(static options => options.Enabled = true));

        (await ReadTenantAsync(host, header: "kiraci-b")).ShouldBe("default");
    }

    [Fact]
    public async Task Baslik_cozumu_acikken_kiraci_okunur()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        (await ReadTenantAsync(host, header: "kiraci-b")).ShouldBe("kiraci-b");
    }

    [Fact]
    public async Task Gecersiz_bicimli_kiraci_varsayilana_duser()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        (await ReadTenantAsync(host, header: "kiraci b/../yonetici")).ShouldBe("default");
    }

    [Fact]
    public async Task Beyaz_liste_disindaki_kiraci_varsayilana_DUSMEZ()
    {
        // Beyaz liste doluyken listede olmayan bir deger varsayilan kiraciya
        // dusmemelidir: dusmek, yetkisiz bir istegin varsayilan kiracinin
        // verisini gormesi demekti. Istek SESSIZCE varsayilana dusmek yerine
        // 403 ile REDDEDILIR (AgentPrismEndpointFilter.CheckTenancyWhitelist).
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
                options.AllowedTenants.Add("kiraci-a");
            }));

        using var rejected = await host.Client.SendAsync(
            Request(HttpMethod.Get, "/agentprism/api/tenants/current", "kiraci-b", body: null));

        rejected.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await ReadTenantAsync(host, header: "kiraci-a")).ShouldBe("kiraci-a");
    }

    [Fact]
    public async Task Claim_ayarliyken_baslik_hic_okunmaz()
    {
        // Guvenligin kalbi burasi. Claim yapilandirilmissa baslik gormezden
        // gelinir; kimlik dogrulamasindan gecmis bir kullanici baslik ekleyerek
        // kiraci degistiremez.
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.ClaimType = "tenant_id";
                options.AllowHeaderResolution = true;
            }),
            // Kullanicida 'tenant_id' claim'i YOKTUR: claim ayarliyken cozumleme
            // varsayilana dusmeli, basliga geri DONMEMELIDIR.
            configureServices: static services => TestAuthenticationHandler.Add(services));

        (await ReadTenantAsync(host, header: "kiraci-b")).ShouldBe("default");
    }

    [Fact]
    public async Task Kiraci_verisi_baska_kiraciya_sizmaz()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        using var created = await host.Client.SendAsync(
            Request(HttpMethod.Put, "/agentprism/api/mcp-servers/gizli", "kiraci-a", new
            {
                endpoint = "https://mcp.example.com/mcp",
                enabled = true,
                requiresApproval = true,
            }));

        created.EnsureSuccessStatusCode();

        using var mine = await host.Client.SendAsync(
            Request(HttpMethod.Get, "/agentprism/api/mcp-servers", "kiraci-a", body: null));

        (await mine.Content.ReadFromJsonAsync<List<McpServerDefinition>>())
            .ShouldNotBeNull()
            .ShouldHaveSingleItem()
            .Name.ShouldBe("gizli");

        using var theirs = await host.Client.SendAsync(
            Request(HttpMethod.Get, "/agentprism/api/mcp-servers", "kiraci-b", body: null));

        (await theirs.Content.ReadFromJsonAsync<List<McpServerDefinition>>())
            .ShouldNotBeNull()
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task Onay_kurali_baska_kiraciya_sizmaz()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        var store = host.Services.GetRequiredService<IToolApprovalRuleStore>();

        await store.AddAsync(new ToolApprovalRule
        {
            Id = AgentPrismId.NewId(),
            TenantId = "kiraci-a",
            ToolName = "cancel_order",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        using var mine = await host.Client.SendAsync(
            Request(HttpMethod.Get, "/agentprism/api/approvals/rules", "kiraci-a", body: null));

        (await mine.Content.ReadFromJsonAsync<List<ToolApprovalRule>>())
            .ShouldNotBeNull()
            .Count.ShouldBe(1);

        using var theirs = await host.Client.SendAsync(
            Request(HttpMethod.Get, "/agentprism/api/approvals/rules", "kiraci-b", body: null));

        (await theirs.Content.ReadFromJsonAsync<List<ToolApprovalRule>>())
            .ShouldNotBeNull()
            .ShouldBeEmpty();
    }

    [Fact]
    public void Kiraci_kimligi_bicimi_dogrulanir()
    {
        HttpTenantContext.IsValidTenantId("acme").ShouldBeTrue();
        HttpTenantContext.IsValidTenantId("acme-1.uretim_2").ShouldBeTrue();

        HttpTenantContext.IsValidTenantId(null).ShouldBeFalse();
        HttpTenantContext.IsValidTenantId("").ShouldBeFalse();
        HttpTenantContext.IsValidTenantId("bosluk var").ShouldBeFalse();
        HttpTenantContext.IsValidTenantId("yol/gecisi").ShouldBeFalse();
        HttpTenantContext.IsValidTenantId(new string('a', 65)).ShouldBeFalse();
    }

    private static async Task<string> ReadTenantAsync(AgentPrismTestHost host, string header)
    {
        using var response = await host.Client.SendAsync(
            Request(HttpMethod.Get, "/agentprism/api/tenants/current", header, body: null));

        response.EnsureSuccessStatusCode();

        var current = await response.Content.ReadFromJsonAsync<CurrentTenantResponse>();

        return current.ShouldNotBeNull().TenantId;
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string tenant, object? body)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));

        request.Headers.Add(TenantHeader, tenant);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }
}
