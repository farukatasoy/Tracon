using System.Net.Http.Headers;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Verifies that the bearer token never appears in any output AgentPrism
/// gives out.
/// </summary>
/// <remarks>
/// The protected boundary is this: the token is compared only against the
/// incoming request's header. The options object, the <c>/api/meta</c>
/// output, error responses, and log lines must <strong>never, under any
/// condition,</strong> carry the token.
/// </remarks>
public sealed class SecretLeakTests
{
    private const string Token = "very-secret-token-TEST-91af3c";

    [Fact]
    public void Options_object_does_not_define_its_own_ToString_method()
    {
        // The options object must NOT be a record: the compiler-generated
        // ToString would print every property and leak the token in the
        // very first log line.
        typeof(AgentPrismEndpointOptions).GetMethod(nameof(ToString), Type.EmptyTypes)!
            .DeclaringType.ShouldBe(typeof(object));
    }

    [Fact]
    public async Task Meta_output_does_not_contain_the_token()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.AuthToken = Token);

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/meta", UriKind.Relative));

        (await response.Content.ReadAsStringAsync()).ShouldNotContain(Token);
    }

    [Fact]
    public async Task Rejected_requests_response_does_not_contain_the_token()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.AuthToken = Token);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");

        using var response = await host.Client.SendAsync(request);

        (await response.Content.ReadAsStringAsync()).ShouldNotContain(Token);
    }

    [Fact]
    public async Task Log_lines_do_not_contain_the_token()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.AuthToken = Token);

        using (var rejected = await host.Client.GetAsync(new Uri("/agentprism/api/agents", UriKind.Relative)))
        {
            rejected.Dispose();
        }

        using var accepted = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        accepted.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        using (var response = await host.Client.SendAsync(accepted))
        {
            response.EnsureSuccessStatusCode();
        }

        host.Logs.AllText.ShouldNotContain(Token);
    }

    [Fact]
    public async Task Running_a_code_agent_does_not_leak_the_token()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            static options =>
            {
                options.AuthToken = Token;
                options.AllowRemoteAccess = true;
            });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/agentprism/api/agents/kod-agent/run")
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "merhaba", SessionId = "s-secret" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        using (var response = await host.Client.SendAsync(request))
        {
            response.EnsureSuccessStatusCode();
            (await response.Content.ReadAsStringAsync()).ShouldNotContain(Token);
        }

        // Session state, run records, and logs must also be clean.
        using var sessionRequest = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/sessions/s-secret");
        sessionRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        using (var session = await host.Client.SendAsync(sessionRequest))
        {
            (await session.Content.ReadAsStringAsync()).ShouldNotContain(Token);
        }

        using var runsRequest = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/runs");
        runsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        using (var runs = await host.Client.SendAsync(runsRequest))
        {
            (await runs.Content.ReadAsStringAsync()).ShouldNotContain(Token);
        }

        host.Logs.AllText.ShouldNotContain(Token);
    }

    // --- Phase 53: API key ---

    [Fact]
    public async Task Raw_api_key_does_not_appear_in_the_listing()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "ci", "AgentsRead");

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/api-keys", UriKind.Relative));

        (await response.Content.ReadAsStringAsync()).ShouldNotContain(created.PlaintextKey);
    }

    [Fact]
    public async Task Raw_api_key_does_not_appear_in_the_audit_trail()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "ci", "AgentsRead");

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/audit", UriKind.Relative));

        (await response.Content.ReadAsStringAsync()).ShouldNotContain(created.PlaintextKey);
    }

    [Fact]
    public async Task Raw_api_key_does_not_appear_in_the_rejected_requests_response()
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

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain(created.PlaintextKey);
    }

    [Fact]
    public async Task Raw_api_key_does_not_appear_in_log_lines()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "ci", "AgentsRead");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using (var response = await host.Client.SendAsync(request))
        {
            response.EnsureSuccessStatusCode();
        }

        host.Logs.AllText.ShouldNotContain(created.PlaintextKey);
    }
}
