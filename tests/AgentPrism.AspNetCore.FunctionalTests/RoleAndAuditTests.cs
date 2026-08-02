using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Faz 9 — rol tabanli yetkilendirme ve denetim izi.
/// </summary>
public sealed class RoleAndAuditTests
{
    private static readonly Uri Agents = new("/agentprism/api/agents", UriKind.Relative);

    // --- Rol modeli: policy kayitli degilse eski davranis ---

    [Fact]
    public async Task Rol_policy_kayitli_degilse_tum_uclar_calisir()
    {
        // Hicbir AgentPrism.Reader/Operator/Admin policy'si kaydedilmedi.
        // K-042'nin ayni gerekcesi: rol modeli, guncelleyen kurulumlari kirmamalidir.
        await using var host = await AgentPrismTestHost.StartAsync();

        using var list = await host.Client.GetAsync(Agents);
        list.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request());
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var deleted = await host.Client.DeleteAsync(new Uri("/agentprism/api/agents/db-agent", UriKind.Relative));
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Admin_policy_basarisizsa_yazma_engellenir_ama_okuma_calisir()
    {
        // Yalnizca Admin policy'si kayitli; Reader hicbir zaman kayitli degil.
        // Rol ayrimini VE fallback'i ayni testte gosterir.
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(AgentPrismPolicies.Admin, static policy => policy.RequireAssertion(static _ => false)));

        using var list = await host.Client.GetAsync(Agents);
        list.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request());
        created.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_policy_basariliysa_yazma_calisir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(AgentPrismPolicies.Admin, static policy => policy.RequireAssertion(static _ => true)));

        using var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request());
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Operator_calistirma_baslatabilir_ama_admin_ucuna_erisemez()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(AgentPrismPolicies.Operator, static policy => policy.RequireAssertion(static _ => true))
                .AddPolicy(AgentPrismPolicies.Admin, static policy => policy.RequireAssertion(static _ => false)));

        using var run = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/agents/kod-agent/run", UriKind.Relative),
            new { message = "merhaba" });
        run.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var create = await host.Client.PostAsJsonAsync(Agents, TestData.Request());
        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- RequireRolePolicies: uretim guvenligi ---

    [Fact]
    public async Task RequireRolePolicies_acikken_eksik_policy_acilista_hata_verir()
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => AgentPrismTestHost.StartAsync(
                configureEndpoints: static options => options.RequireRolePolicies = true));

        exception.Message.ShouldContain(AgentPrismPolicies.Reader);
        exception.Message.ShouldContain(AgentPrismPolicies.Operator);
        exception.Message.ShouldContain(AgentPrismPolicies.Admin);
    }

    [Fact]
    public async Task RequireRolePolicies_acikken_ucu_policy_tanimliysa_basarili_acilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.RequireRolePolicies = true,
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(AgentPrismPolicies.Reader, static policy => policy.RequireAssertion(static _ => true))
                .AddPolicy(AgentPrismPolicies.Operator, static policy => policy.RequireAssertion(static _ => true))
                .AddPolicy(AgentPrismPolicies.Admin, static policy => policy.RequireAssertion(static _ => true)));

        using var response = await host.Client.GetAsync(Agents);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // --- /api/audit ---

    [Fact]
    public async Task Audit_ucu_admin_policy_ile_korunur()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(AgentPrismPolicies.Admin, static policy => policy.RequireAssertion(static _ => false)));

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/audit", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Agent_yazma_islemleri_denetim_izine_dusuyor()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using (var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request()))
        {
            created.EnsureSuccessStatusCode();
        }

        using (var deleted = await host.Client.DeleteAsync(
            new Uri("/agentprism/api/agents/db-agent", UriKind.Relative)))
        {
            deleted.EnsureSuccessStatusCode();
        }

        using var audit = await host.Client.GetAsync(new Uri("/agentprism/api/audit?entity=agent:db-agent", UriKind.Relative));
        audit.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = (await AgentPrismTestHost.ReadJsonAsync(audit)).EnumerateArray()
            .Select(static entry => entry.GetProperty("action").GetString())
            .ToList();

        entries.ShouldContain(static action => string.Equals(action, "agent.create", StringComparison.Ordinal));
        entries.ShouldContain(static action => string.Equals(action, "agent.delete", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Entity_gecmisi_ucu_tek_varligi_dondurur()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using (var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request()))
        {
            created.EnsureSuccessStatusCode();
        }

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/audit/agent:db-agent", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = (await AgentPrismTestHost.ReadJsonAsync(response)).EnumerateArray().ToList();

        entries.ShouldHaveSingleItem().GetProperty("entity").GetString().ShouldBe("agent:db-agent");
    }

    // --- /api/meta rol bilgisi ---

    [Fact]
    public async Task Meta_policy_kayitli_degilse_rolleri_acik_bildirir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/meta", UriKind.Relative));
        var roles = (await AgentPrismTestHost.ReadJsonAsync(response)).GetProperty("roles");

        roles.GetProperty("canRead").GetBoolean().ShouldBeTrue();
        roles.GetProperty("canOperate").GetBoolean().ShouldBeTrue();
        roles.GetProperty("canAdminister").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Meta_admin_policy_basarisizsa_canAdminister_false_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(AgentPrismPolicies.Admin, static policy => policy.RequireAssertion(static _ => false)));

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/meta", UriKind.Relative));
        var roles = (await AgentPrismTestHost.ReadJsonAsync(response)).GetProperty("roles");

        roles.GetProperty("canAdminister").GetBoolean().ShouldBeFalse();
        roles.GetProperty("canRead").GetBoolean().ShouldBeTrue();
    }
}
