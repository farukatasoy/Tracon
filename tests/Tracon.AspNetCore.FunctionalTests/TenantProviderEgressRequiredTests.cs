using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 259 (F-254): <c>Tracon:TenantProviders:RequireEgressPolicy</c> turns "a tenant with no egress
/// policy may call every provider" into "a tenant with no egress policy may call none".
/// </summary>
/// <remarks>
/// <para>
/// The model path and the binding endpoint are asserted here through the real host. The image,
/// speech, live voice and embedding paths each carry one case in the class that already owns their
/// host (<c>ImageTenantPolicyTests</c>, <c>SpeechTenantPolicyTests</c>,
/// <c>LiveVoiceTenantPolicyTests</c>, <c>KnowledgeTenantPolicyTests</c>): the rule is one function,
/// but a caller that never passes it the switch is a silent fail-open, which only a call through
/// that caller shows.
/// </para>
/// <para>
/// Requests carry no tenant header, so they run as the default tenant. With the switch on the
/// default tenant needs a policy row like any other.
/// </para>
/// </remarks>
public sealed class TenantProviderEgressRequiredTests
{
    private const string AgentName = "egress-required-agent";
    private const string ProviderName = "egress-required-provider";
    private const string Setting = "Tracon:TenantProviders:RequireEgressPolicy";

    private static readonly Uri EgressUri = new("/tracon/api/tenants/default/egress", UriKind.Relative);

    [Fact]
    public async Task With_the_switch_on_a_tenant_without_a_policy_is_refused_and_the_message_names_the_switch()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider, requirePolicy: true);

        // A policy exists while the agent is created and run once, so the agent is in the compiled-agent
        // cache; deleting the policy then leaves the tenant with none.
        await SavePolicyAsync(host, ProviderName);
        await CreateAgentAsync(host);

        using (var first = await RunAsync(host))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var callsBeforeDelete = provider.Requests.Count;

        using (var deleted = await host.Client.DeleteAsync(EgressUri, TestContext.Current.CancellationToken))
        {
            deleted.EnsureSuccessStatusCode();
        }

        using var refused = await RunAsync(host);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("has no egress policy");
        body.ShouldContain(Setting);
        body.ShouldContain($"model provider '{ProviderName}'");
        provider.Requests.Count.ShouldBe(callsBeforeDelete, "the tenant's message must not reach a provider the tenant has no policy for");
    }

    [Fact]
    public async Task With_the_switch_on_a_fresh_tenant_cannot_create_an_agent_on_a_provider()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider, requirePolicy: true);

        using var response = await CreateAgentRawAsync(host);

        response.StatusCode.ShouldNotBe(HttpStatusCode.Created);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain(Setting);
        provider.Requests.Count.ShouldBe(0);
    }

    [Fact]
    public async Task With_the_switch_off_a_tenant_without_a_policy_reaches_every_provider()
    {
        // K1: nothing changes until the switch is turned on.
        var provider = Provider();
        await using var host = await StartAsync(provider, requirePolicy: false);

        await CreateAgentAsync(host);

        using var response = await RunAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        provider.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_tenant_with_a_policy_is_unaffected_by_the_switch()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider, requirePolicy: true);

        await SavePolicyAsync(host, ProviderName);
        await CreateAgentAsync(host);

        using var response = await RunAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        provider.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_empty_policy_still_refuses_every_provider_with_the_switch_on()
    {
        // "Nothing is allowed" and "no policy" are different rows: the first has the allow-list message.
        var provider = Provider();
        await using var host = await StartAsync(provider, requirePolicy: true);

        await SavePolicyAsync(host);

        using var response = await CreateAgentRawAsync(host);

        response.StatusCode.ShouldNotBe(HttpStatusCode.Created);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("is not allowed to call");
        body.ShouldNotContain("has no egress policy");
    }

    [Fact]
    public async Task Saving_a_binding_without_a_policy_is_refused_by_name()
    {
        // The binding endpoint judged a missing policy as "allowed", and its refusal path unwrapped the
        // policy with `!`: a tenant with none reaches it now, so the message must be built without one.
        var provider = Provider();
        await using var host = await StartAsync(provider, requirePolicy: true);

        using var refused = await SaveBindingAsync(host);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("has no egress policy");
        body.ShouldContain(Setting);

        // Once the tenant has a policy that names the provider, the same request saves.
        await SavePolicyAsync(host, ProviderName);

        using var saved = await SaveBindingAsync(host);

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task With_the_switch_off_a_binding_saves_without_a_policy()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider, requirePolicy: false);

        using var saved = await SaveBindingAsync(host);

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static FakeModelProvider Provider()
        => new FakeModelProvider(ProviderName)
            .WithModel(new ModelDescriptor { Name = "egress-model" })
            .EchoesUserMessage();

    private static Task<TraconTestHost> StartAsync(FakeModelProvider provider, bool requirePolicy)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder.AddModelProvider(provider),
            configureServices: services => services.Configure<TraconTenantProviderOptions>(
                options => options.RequireEgressPolicy = requirePolicy));

    private static async Task SavePolicyAsync(TraconTestHost host, params string[] allowed)
    {
        using var response = await host.Client.PutAsJsonAsync(
            EgressUri,
            new { allowedProviders = allowed },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private static Task<HttpResponseMessage> CreateAgentRawAsync(TraconTestHost host)
        => host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents", UriKind.Relative),
            new AgentDefinitionRequest
            {
                Name = AgentName,
                Instructions = "Reply briefly.",
                Model = new ModelBinding { Provider = ProviderName, Model = "egress-model" },
            },
            TestContext.Current.CancellationToken);

    private static async Task CreateAgentAsync(TraconTestHost host)
    {
        using var response = await CreateAgentRawAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<HttpResponseMessage> RunAsync(TraconTestHost host)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hi" }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        return await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> SaveBindingAsync(TraconTestHost host)
        => host.Client.PutAsJsonAsync(
            new Uri($"/tracon/api/tenants/default/providers/{ProviderName}", UriKind.Relative),
            new { apiKeyConfigurationName = "Tracon:ProviderKeys:default:EgressRequired" },
            TestContext.Current.CancellationToken);
}
