using System.Net;
using System.Net.Http.Json;
using Tracon.Testing;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A tenant egress policy saved while an agent sits in the compiled-agent
/// cache reaches the very next run (F-304).
/// </summary>
/// <remarks>
/// 🚨 Regression (2026-10-11): the policy is checked only when a model client
/// is built, and the cache key carried no trace of it. After the first run had
/// cached the agent, saving a policy that forbids the agent's provider changed
/// nothing: the next run went to that provider with a 200.
/// </remarks>
public sealed class EgressPolicyCacheTests
{
    private const string AgentName = "egress-agent";
    private const string ProviderName = "egress-provider";

    [Fact]
    public async Task A_policy_saved_after_a_run_refuses_the_next_run_before_any_model_call()
    {
        var provider = new FakeModelProvider(ProviderName)
            .WithModel(new ModelDescriptor { Name = "egress-model" })
            .EchoesUserMessage();

        await using var host = await Infrastructure.TraconTestHost.StartAsync(
            configureTracon: builder => builder.AddModelProvider(provider));

        await CreateAsync(host);

        using (var first = await RunAsync(host))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var callsBeforePolicy = provider.Requests.Count;

        // The request carries no tenant header, so it runs as the default
        // tenant. "echo" is the host's other registered provider: the policy
        // is valid, it only leaves this agent's provider out.
        using (var saved = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/tenants/default/egress", UriKind.Relative),
            new { allowedProviders = new[] { "echo" } },
            TestContext.Current.CancellationToken))
        {
            saved.EnsureSuccessStatusCode();
        }

        using var refused = await RunAsync(host);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .ShouldContain($"not allowed to call model provider '{ProviderName}'");
        provider.Requests.Count.ShouldBe(callsBeforePolicy);
    }

    private static async Task CreateAsync(Infrastructure.TraconTestHost host)
    {
        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents", UriKind.Relative),
            new AgentDefinitionRequest
            {
                Name = AgentName,
                Instructions = "Reply briefly.",
                Model = new ModelBinding { Provider = ProviderName, Model = "egress-model" },
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static async Task<HttpResponseMessage> RunAsync(Infrastructure.TraconTestHost host)
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
}
