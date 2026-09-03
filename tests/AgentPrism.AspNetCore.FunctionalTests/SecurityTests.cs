using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Verifies each layer of the three-layer access protection separately.
/// </summary>
public sealed class SecurityTests
{
    private const string Token = "very-secret-token-TEST-4c1f8a";

    // --- Layer 1: loopback restriction ---

    [Fact]
    public async Task Remote_ip_is_rejected_by_default()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        request.Headers.Add(AgentPrismTestHost.RemoteIpHeader, "203.0.113.7");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Loopback_ip_passes()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        request.Headers.Add(AgentPrismTestHost.RemoteIpHeader, "127.0.0.1");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task IPv6_loopback_passes()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        request.Headers.Add(AgentPrismTestHost.RemoteIpHeader, "::1");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task IPv4_loopback_mapped_to_IPv6_passes()
    {
        // ::ffff:127.0.0.1 — IPAddress.IsLoopback does not recognize this form on its own.
        await using var host = await AgentPrismTestHost.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        request.Headers.Add(AgentPrismTestHost.RemoteIpHeader, "::ffff:127.0.0.1");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Remote_ip_passes_when_remote_access_is_enabled()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.AllowRemoteAccess = true);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        request.Headers.Add(AgentPrismTestHost.RemoteIpHeader, "203.0.113.7");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Shell_still_loads_on_a_non_loopback_request_HATA_S4_003()
    {
        // MT-UI-008: the shell must also be exempt from the loopback
        // restriction — otherwise a non-loopback client can never download
        // the JS bundle, AccessGate never runs, and the user is left with
        // raw ProblemDetails JSON instead of the "Access denied" card. The
        // data endpoints (the second request below) must remain protected;
        // the real denial message comes from there.
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => services.AddSingleton<IAgentPrismUiProvider, FakeUiProvider>());

        using var shellRequest = new HttpRequestMessage(HttpMethod.Get, "/agentprism/");
        shellRequest.Headers.Add(AgentPrismTestHost.RemoteIpHeader, "203.0.113.7");

        using var shellResponse = await host.Client.SendAsync(shellRequest);

        shellResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var dataRequest = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        dataRequest.Headers.Add(AgentPrismTestHost.RemoteIpHeader, "203.0.113.7");

        using var dataResponse = await host.Client.SendAsync(dataRequest);

        dataResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- Layer 2: bearer token ---

    [Fact]
    public async Task Headerless_request_is_rejected_when_a_token_is_required()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.AuthToken = Token);

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/agents", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().ShouldContain("Bearer", Case.Sensitive);
    }

    [Fact]
    public async Task Wrong_token_is_rejected()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.AuthToken = Token);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-token");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Wrong_token_of_the_same_length_is_rejected()
    {
        // Shows the constant-time comparison also gives the right result
        // when the lengths match: only the last character differs.
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.AuthToken = Token);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            string.Concat(Token.AsSpan(0, Token.Length - 1), "b"));

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Correct_token_passes()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.AuthToken = Token);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_scheme_other_than_Bearer_is_rejected()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.AuthToken = Token);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Token);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // --- Layer 3: authorization policy ---

    [Fact]
    public async Task Failing_policy_blocks_access()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.RequireAuthorization("AgentPrismAdmin"),
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy("AgentPrismAdmin", static policy => policy.RequireAssertion(static _ => false)));

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/agents", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Succeeding_policy_allows_access()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.RequireAuthorization("AgentPrismAdmin"),
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy("AgentPrismAdmin", static policy => policy.RequireAssertion(static _ => true)));

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/agents", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // --- Meta endpoint's exemption ---

    [Fact]
    public async Task Meta_endpoint_is_open_even_when_a_token_is_required()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.AuthToken = Token);

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/meta", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Meta_endpoint_is_open_from_a_remote_ip_too()
    {
        // If the UI can never discover which identity method to use, it can
        // never sign in. So the meta endpoint is also exempt from the
        // loopback restriction.
        await using var host = await AgentPrismTestHost.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/meta");
        request.Headers.Add(AgentPrismTestHost.RemoteIpHeader, "203.0.113.7");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Meta_endpoint_is_open_even_under_a_failing_policy()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.RequireAuthorization("AgentPrismAdmin"),
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy("AgentPrismAdmin", static policy => policy.RequireAssertion(static _ => false)));

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/meta", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OpenAI_compatible_endpoints_are_also_protected()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.AuthToken = Token);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/v1/responses", UriKind.Relative),
            new { model = "does-not-exist", input = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
