using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Tracon.AGUI.FunctionalTests.Infrastructure;

namespace Tracon.AGUI.FunctionalTests;

/// <summary>
/// The AG-UI endpoint is a second way to start a run, and it passes through the same doors as the first.
/// </summary>
/// <remarks>
/// The claim is not that a filter exists but that a front end cannot reach an agent through this endpoint that it could not
/// reach through <c>POST /api/agents/{name}/run</c>: loopback by default, the bearer token, the authorization policy, the
/// <c>Operator</c> role and the <c>runs:write</c> key scope.
/// </remarks>
public sealed class AGUIEndpointProtectionTests
{
    private const string RemoteIpHeader = "X-Test-Remote-Ip";
    private const string Token = "agui-test-token";

    private static void SimulateRemoteAddresses(WebApplication app)
        => app.Use(static (context, next) =>
        {
            if (context.Request.Headers.TryGetValue(RemoteIpHeader, out var raw) &&
                System.Net.IPAddress.TryParse(raw.ToString(), out var address))
            {
                context.Connection.RemoteIpAddress = address;
            }

            return next(context);
        });

    [Fact]
    public async Task The_endpoint_needs_the_management_API_mapped_first()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddTracon();

        await using var app = builder.Build();

        var failure = Should.Throw<InvalidOperationException>(() => app.MapTraconAGUI());

        failure.Message.ShouldContain("MapTracon", Case.Sensitive, "the access protection is inherited from there");
    }

    [Fact]
    public async Task A_remote_address_is_refused_by_default()
    {
        await using var host = await AGUITestHost.StartAsync(TestAgents.AddEcho, configureApp: SimulateRemoteAddresses);

        var response = await host.RunAsync(
            TestAgents.Echo,
            AGUIInputs.User("t-remote", "hi"),
            request => request.Headers.Add(RemoteIpHeader, "203.0.113.9"));

        response.Status.ShouldBe(HttpStatusCode.Forbidden, response.Body);

        var local = await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-remote", "hi"));
        local.Status.ShouldBe(HttpStatusCode.OK, "loopback is the default and needs nothing");
    }

    [Fact]
    public async Task The_bearer_token_is_required_when_one_is_configured()
    {
        await using var host = await AGUITestHost.StartAsync(
            TestAgents.AddEcho,
            configureEndpoints: static options => options.AuthToken = Token);

        var anonymous = await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-token", "hi"));
        anonymous.Status.ShouldBe(HttpStatusCode.Unauthorized, anonymous.Body);

        var wrong = await host.RunAsync(
            TestAgents.Echo,
            AGUIInputs.User("t-token", "hi"),
            request => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-token"));
        wrong.Status.ShouldBe(HttpStatusCode.Unauthorized);

        var right = await host.RunAsync(
            TestAgents.Echo,
            AGUIInputs.User("t-token", "hi"),
            request => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token));
        right.Status.ShouldBe(HttpStatusCode.OK, right.Body);
    }

    [Fact]
    public async Task The_operator_role_is_required()
    {
        await using var withoutPolicies = await AGUITestHost.StartAsync(
            TestAgents.AddEcho,
            configureServices: static services => services
                .AddAuthentication("Test")
                .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", configureOptions: null));

        // No role policy is registered: the documented fallback adds no role check, so the run goes through.
        (await withoutPolicies.RunAsync(TestAgents.Echo, AGUIInputs.User("t-role", "hi"))).Status.ShouldBe(HttpStatusCode.OK);

        await using var forbidden = await AGUITestHost.StartAsync(
            TestAgents.AddEcho,
            configureServices: static services =>
            {
                services
                    .AddAuthentication("Test")
                    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", configureOptions: null);
                services.AddAuthorizationBuilder()
                    .AddPolicy(TraconPolicies.Reader, static policy => policy.RequireAssertion(static _ => true))
                    .AddPolicy(TraconPolicies.Operator, static policy => policy.RequireAssertion(static _ => false));
            });

        var response = await forbidden.RunAsync(TestAgents.Echo, AGUIInputs.User("t-role", "hi"));

        response.Status.ShouldBe(HttpStatusCode.Forbidden, "a reader may look, an operator may run: this is a run");
    }

    [Fact]
    public async Task An_API_key_needs_the_runs_write_scope()
    {
        await using var host = await AGUITestHost.StartAsync(TestAgents.AddEcho);

        var readOnly = await CreateKeyAsync(host, "RunsRead");
        var writer = await CreateKeyAsync(host, "RunsWrite");

        var refused = await host.RunAsync(
            TestAgents.Echo,
            AGUIInputs.User("t-key", "hi"),
            request => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", readOnly));
        refused.Status.ShouldBe(HttpStatusCode.Forbidden, refused.Body);

        var allowed = await host.RunAsync(
            TestAgents.Echo,
            AGUIInputs.User("t-key", "hi"),
            request => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", writer));
        allowed.Status.ShouldBe(HttpStatusCode.OK, allowed.Body);
    }

    [Theory]
    [InlineData("/tracon/agui")]
    [InlineData("/agui")]
    public async Task A_browser_origin_in_the_allowed_list_is_answered_on_the_AG_UI_path_and_no_other_is(string pattern)
    {
        // A front end runs on another origin more often than not. The path gets the answer the management API gives,
        // whether it sits under MapTracon's prefix or beside it, and never a wider one.
        await using var host = await AGUITestHost.StartAsync(
            TestAgents.AddEcho,
            configureEndpoints: static options => options.AllowedOrigins.Add("https://shop.example.com"),
            pattern: pattern);

        using var allowed = new HttpRequestMessage(HttpMethod.Options, new Uri($"{pattern}/{TestAgents.Echo}", UriKind.Relative));
        allowed.Headers.Add("Origin", "https://shop.example.com");
        allowed.Headers.Add("Access-Control-Request-Method", "POST");
        allowed.Headers.Add("Access-Control-Request-Headers", "content-type,authorization");

        using var granted = await host.Client.SendAsync(allowed, TestContext.Current.CancellationToken);
        granted.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe(["https://shop.example.com"]);

        using var other = new HttpRequestMessage(HttpMethod.Options, new Uri($"{pattern}/{TestAgents.Echo}", UriKind.Relative));
        other.Headers.Add("Origin", "https://evil.example.com");
        other.Headers.Add("Access-Control-Request-Method", "POST");

        using var refused = await host.Client.SendAsync(other, TestContext.Current.CancellationToken);
        refused.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    private static async Task<string> CreateKeyAsync(AGUITestHost host, params string[] scopes)
    {
        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/api-keys", UriKind.Relative),
            new { name = $"agui-{string.Join('-', scopes)}", scopes },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("plaintextKey").GetString()!;
    }
}
