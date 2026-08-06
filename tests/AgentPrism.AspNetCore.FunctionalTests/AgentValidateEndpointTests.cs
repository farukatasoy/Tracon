using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// F-60 — <c>POST /api/agents/validate</c>: bir tanimi kaydetmeden ve hicbir
/// model cagirmadan derler.
/// </summary>
public sealed class AgentValidateEndpointTests
{
    private static readonly Uri Validate = new("/agentprism/api/agents/validate", UriKind.Relative);
    private static readonly Uri Agents = new("/agentprism/api/agents", UriKind.Relative);
    private static readonly Uri Runs = new("/agentprism/api/runs", UriKind.Relative);

    [Fact]
    public async Task Gecerli_tanim_200_ve_valid_true_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Validate, TestData.Request());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await AgentPrismTestHost.ReadJsonAsync(response);
        json.GetProperty("valid").GetBoolean().ShouldBeTrue();
        json.GetProperty("inconclusive").GetBoolean().ShouldBeFalse();
        json.GetProperty("messages").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Bilinmeyen_tool_200_ve_valid_false_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            Validate,
            TestData.Request() with { ToolNames = ["olmayan_tool"] });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await AgentPrismTestHost.ReadJsonAsync(response);
        json.GetProperty("valid").GetBoolean().ShouldBeFalse();

        var codes = json.GetProperty("messages").EnumerateArray()
            .Select(static message => message.GetProperty("code").GetString())
            .ToArray();
        codes.ShouldBe(["unknown_tool"]);
    }

    [Fact]
    public async Task Kendini_cagiran_tanim_cycle_koduyla_gecersiz_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            Validate,
            TestData.Request() with { CallableAgentNames = ["db-agent"] });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await AgentPrismTestHost.ReadJsonAsync(response);
        json.GetProperty("valid").GetBoolean().ShouldBeFalse();
        json.GetProperty("messages")[0].GetProperty("code").GetString().ShouldBe("cycle");
    }

    [Fact]
    public async Task Bos_isim_400_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Validate, TestData.Request() with { Name = "" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reader_ucu_kullanamaz_operator_kullanabilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(AgentPrismPolicies.Reader, static policy => policy.RequireAssertion(static _ => true))
                .AddPolicy(AgentPrismPolicies.Operator, static policy => policy.RequireAssertion(static _ => false)));

        using var forbidden = await host.Client.PostAsJsonAsync(Validate, TestData.Request());
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Dogrulama_sonrasi_katalog_ve_runs_degismez()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var beforeAgents = (await AgentPrismTestHost.ReadJsonAsync(await host.Client.GetAsync(Agents))).GetArrayLength();
        var beforeRuns = (await AgentPrismTestHost.ReadJsonAsync(await host.Client.GetAsync(Runs))).GetArrayLength();

        using var valid = await host.Client.PostAsJsonAsync(Validate, TestData.Request());
        valid.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var invalid = await host.Client.PostAsJsonAsync(
            Validate,
            TestData.Request() with { ToolNames = ["olmayan_tool"] });
        invalid.StatusCode.ShouldBe(HttpStatusCode.OK);

        var afterAgents = (await AgentPrismTestHost.ReadJsonAsync(await host.Client.GetAsync(Agents))).GetArrayLength();
        var afterRuns = (await AgentPrismTestHost.ReadJsonAsync(await host.Client.GetAsync(Runs))).GetArrayLength();

        afterAgents.ShouldBe(beforeAgents);
        afterRuns.ShouldBe(beforeRuns);
    }
}
