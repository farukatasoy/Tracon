using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Two tenants carry a skill of the same name and different text, and one agent
/// definition names it. Each tenant's run is offered its own skill (the name and
/// description in the prompt) and never the other's, on a plain agent and on a
/// harness agent (F-578, Phase 219).
/// </summary>
/// <remarks>
/// The tenant is a security boundary and the skill chain keeps a cache of the
/// skill list. The chain is built in one place for both agent types, so one test
/// body covers both: the HTTP run, the tenant header, the compiled-agent cache,
/// Microsoft Agent Framework's skill provider and the skill list in the prompt.
/// The compiled-agent cache already keeps a tenant's agent apart, so this test
/// does not see the chain's own cache key; <c>SkillChainTests</c> in the unit
/// tests does, with one agent and two tenants.
/// </remarks>
public sealed class SkillChainTenantIsolationTests
{
    private const string AgentName = "toned";
    private const string SkillName = "tone";
    private const string TenantHeader = "X-Tracon-Tenant";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Each_tenant_is_offered_its_own_skill(bool harness)
    {
        var provider = new FakeModelProvider("isolation-model").RespondsWith("one", "two", "three");

        await using var host = await TraconTestHost.StartAsync(configureTracon: builder =>
        {
            builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            });
            builder.AddModelProvider(provider);
            builder.AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Answer.",
                Model = new ModelBinding { Provider = "isolation-model", Model = "isolation" },
                SkillNames = [SkillName],
                Harness = harness ? new HarnessSettings() : null,
                Origin = AgentDefinitionOrigin.Code,
            });
        });

        var store = host.Services.GetRequiredService<IAgentSkillStore>();
        await SaveAsync(store, "acme", "TONE-ACME-71c4");
        await SaveAsync(store, "globex", "TONE-GLOBEX-71c4");

        // The third run is acme's again: its compiled agent comes from the cache.
        await RunAsync(host, "acme");
        await RunAsync(host, "globex");
        await RunAsync(host, "acme");

        var requests = provider.Requests;
        requests.Count.ShouldBe(3);
        Prompt(requests[0]).ShouldContain("TONE-ACME-71c4");
        Prompt(requests[0]).ShouldNotContain("TONE-GLOBEX-71c4");
        Prompt(requests[1]).ShouldContain("TONE-GLOBEX-71c4");
        Prompt(requests[1]).ShouldNotContain("TONE-ACME-71c4");
        Prompt(requests[2]).ShouldContain("TONE-ACME-71c4");
        Prompt(requests[2]).ShouldNotContain("TONE-GLOBEX-71c4");
    }

    private static async Task SaveAsync(IAgentSkillStore store, string tenant, string description)
        => await store.SaveAsync(new AgentSkillDefinition
        {
            TenantId = tenant,
            Name = SkillName,
            Description = description,
            Instructions = "Answer in the house tone.",
        });

    /// <summary>Runs the agent once for a tenant and drains the stream.</summary>
    private static async Task RunAsync(TraconTestHost host, string tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "Answer in the house tone." }),
        };
        request.Headers.Add(TenantHeader, tenant);

        using var response = await host.Client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        await foreach (var frame in SseReader.ReadAsync(await response.Content.ReadAsStreamAsync()))
        {
            _ = frame;
        }
    }

    private static string Prompt(Tracon.Testing.FakeModelRequest request)
        => string.Join(
            Environment.NewLine,
            [request.Options?.Instructions ?? string.Empty, .. request.Messages.Select(static message => message.Text)]);
}
