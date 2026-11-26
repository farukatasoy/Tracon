using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracon.AGUI.FunctionalTests.Infrastructure;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AGUI.FunctionalTests;

/// <summary>
/// A host that signs its users in with its own bearer token (a JWT it issued) can send that token to Tracon in the
/// <c>Authorization</c> header, the way its other API takes it.
/// </summary>
/// <remarks>
/// <para>
/// Tracon reads the same header for its own static token and API keys. A header that is neither is the host's when the host's
/// authentication has already identified the caller, and the request is judged as one that carried no header: the host's
/// policies, the role policies, the run gate and the tenant checks decide it. The cases pin both sides of that: the value
/// that is the host's passes, and every value that is not (an anonymous caller's unknown value, a key-shaped value the store
/// refuses, a static token the host did not present) is still refused.
/// </para>
/// <para>
/// The authentication scheme here reads the header the way a JwtBearer handler does: it authenticates a request whose bearer
/// value is a token it issued and says nothing about any other value.
/// </para>
/// </remarks>
public sealed class AGUIHostCredentialTests
{
    private const string HostScheme = "HostBearer";
    private const string HostTokenPrefix = "host-jwt-";
    private const string HostToken = HostTokenPrefix + "a4f19c07e2";
    private const string RemoteIpHeader = "X-Test-Remote-Ip";

    private static void AddHostBearer(IServiceCollection services)
        => services
            .AddAuthentication(HostScheme)
            .AddScheme<AuthenticationSchemeOptions, HostBearerHandler>(HostScheme, configureOptions: null);

    private static void AddAlwaysAuthenticated(IServiceCollection services)
        => services
            .AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", configureOptions: null);

    private static void AddRolePolicies(IServiceCollection services)
        => services.AddAuthorizationBuilder()
            .AddPolicy(TraconPolicies.Reader, static policy => policy.RequireAssertion(static _ => true))
            .AddPolicy(TraconPolicies.Operator, static policy => policy.RequireAssertion(static _ => true))
            .AddPolicy(TraconPolicies.Admin, static policy => policy.RequireAssertion(static _ => true));

    private static void SimulateRemoteAddresses(WebApplication app)
        => app.Use(static (context, next) =>
        {
            if (context.Request.Headers.TryGetValue(RemoteIpHeader, out var raw) &&
                IPAddress.TryParse(raw.ToString(), out var address))
            {
                context.Connection.RemoteIpAddress = address;
            }

            return next(context);
        });

    private static Action<HttpRequestMessage> Bearer(string value)
        => request => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", value);

    private static Action<HttpRequestMessage> Scheme(string scheme, string value)
        => request => request.Headers.Authorization = new AuthenticationHeaderValue(scheme, value);

    [Fact]
    public async Task A_token_the_host_signed_the_caller_in_with_runs_an_agent_and_reads_the_management_API()
    {
        await using var host = await AGUITestHost.StartAsync(
            TestAgents.AddEcho,
            configureServices: static services =>
            {
                AddHostBearer(services);
                AddRolePolicies(services);
            });

        var run = await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-host", "hello"), Bearer(HostToken));

        run.Status.ShouldBe(HttpStatusCode.OK, run.Body);
        run.Text.ShouldBe("Echo: hello");

        using var read = new HttpRequestMessage(HttpMethod.Get, new Uri("/tracon/api/runs", UriKind.Relative));
        Bearer(HostToken)(read);

        using var runs = await host.Client.SendAsync(read, TestContext.Current.CancellationToken);

        runs.StatusCode.ShouldBe(HttpStatusCode.OK, "the management API takes the host's token the way the chat endpoint does");
    }

    [Fact]
    public async Task A_value_nobody_identified_is_still_refused()
    {
        await using var host = await AGUITestHost.StartAsync(
            TestAgents.AddEcho,
            configureServices: static services =>
            {
                AddHostBearer(services);
                AddRolePolicies(services);
            });

        // The host's scheme says nothing about this value, so the caller is anonymous, and a header an anonymous caller
        // sends must name a credential Tracon knows.
        var unknown = await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-unknown", "hi"), Bearer("not-a-token-anybody-issued"));

        unknown.Status.ShouldBe(HttpStatusCode.Unauthorized, unknown.Body);

        var none = await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-none", "hi"));

        none.Status.ShouldBe(HttpStatusCode.OK, "no header from this machine is the local operator, as before");
    }

    [Fact]
    public async Task A_key_shaped_value_is_Tracons_even_for_an_identified_caller()
    {
        // Every request is identified here. A value shaped like a Tracon key that the store does not accept is refused
        // and is not read as the caller's own credential, so a mistyped or revoked key never runs with wider rights.
        await using var host = await AGUITestHost.StartAsync(TestAgents.AddEcho, configureServices: AddAlwaysAuthenticated);

        var bogus = await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-key-bogus", "hi"), Bearer("ap_acme_not-a-key-the-store-issued"));

        bogus.Status.ShouldBe(HttpStatusCode.Unauthorized, bogus.Body);

        var other = await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-key-other", "hi"), Bearer("some-jwt-of-the-host"));

        other.Status.ShouldBe(HttpStatusCode.OK, other.Body);
    }

    [Theory]
    [InlineData("bearer")]
    [InlineData("BEARER")]
    [InlineData("BeArEr")]
    public async Task The_case_of_the_scheme_name_does_not_hide_a_key_shaped_value_from_Tracon(string scheme)
    {
        // The name of an HTTP authentication scheme is not case sensitive. A key-shaped value behind a lower-case scheme is
        // still a key the store did not accept, so an identified caller does not get its own rights through it.
        await using var host = await AGUITestHost.StartAsync(TestAgents.AddEcho, configureServices: AddAlwaysAuthenticated);

        var refused = await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-case-" + scheme, "hi"), Scheme(scheme, "ap_acme_not-a-key-the-store-issued"));

        refused.Status.ShouldBe(HttpStatusCode.Unauthorized, refused.Body);

        var hosts = await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-case-host-" + scheme, "hi"), Scheme(scheme, "some-jwt-of-the-host"));

        hosts.Status.ShouldBe(HttpStatusCode.OK, "a value that is not key-shaped is still the host's, whatever the case of the scheme");
    }

    [Fact]
    public async Task A_valid_key_keeps_its_own_scopes_for_an_identified_caller()
    {
        await using var host = await AGUITestHost.StartAsync(TestAgents.AddEcho, configureServices: AddAlwaysAuthenticated);

        using var created = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/api-keys", UriKind.Relative),
            new { name = "reader", scopes = new[] { "RunsRead" } },
            TestContext.Current.CancellationToken);
        created.EnsureSuccessStatusCode();

        var key = (await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("plaintextKey").GetString()!;

        // The caller the host identified may run agents. The key it presents may not, and the key decides.
        var refused = await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-key-scope", "hi"), Bearer(key));

        refused.Status.ShouldBe(HttpStatusCode.Forbidden, refused.Body);
    }

    [Fact]
    public async Task A_host_token_is_not_looked_up_as_an_API_key()
    {
        await using var host = await AGUITestHost.StartAsync(
            static tracon =>
            {
                TestAgents.AddEcho(tracon);
                tracon.Services.ThrowFrom<IApiKeyStore>(
                    nameof(IApiKeyStore.FindByHashAsync),
                    static () => new InvalidOperationException("The host's token reached the key store."));
            },
            configureServices: static services =>
            {
                AddHostBearer(services);
                AddRolePolicies(services);
            });

        var run = await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-no-lookup", "hi"), Bearer(HostToken));

        run.Status.ShouldBe(HttpStatusCode.OK, run.Body);
    }

    [Fact]
    public async Task A_static_token_is_still_required_and_the_hosts_token_does_not_stand_in_for_it()
    {
        await using var host = await AGUITestHost.StartAsync(
            TestAgents.AddEcho,
            configureServices: static services =>
            {
                AddHostBearer(services);
                AddRolePolicies(services);
            },
            configureEndpoints: static options => options.AuthToken = "static-token");

        var hostToken = await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-static-a", "hi"), Bearer(HostToken));

        hostToken.Status.ShouldBe(HttpStatusCode.Unauthorized, "a configured token is required, as it is for a caller signed in with a cookie");

        var staticToken = await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-static-b", "hi"), Bearer("static-token"));

        staticToken.Status.ShouldBe(HttpStatusCode.OK, staticToken.Body);
    }

    [Fact]
    public async Task With_the_three_role_policies_an_identified_remote_caller_reaches_the_chat_endpoint()
    {
        // Only the role policies, no token and no general policy: the management API already let a remote caller the host
        // identified through. The chat endpoint sits beside it under the same role policies and answers the same way.
        await using var host = await AGUITestHost.StartAsync(
            TestAgents.AddEcho,
            configureServices: static services =>
            {
                AddHostBearer(services);
                AddRolePolicies(services);
            },
            configureEndpoints: static options => options.AllowRemoteAccess = true,
            configureApp: SimulateRemoteAddresses);

        static void RemoteWithHostToken(HttpRequestMessage request)
        {
            request.Headers.Add(RemoteIpHeader, "203.0.113.9");
            Bearer(HostToken)(request);
        }

        var run = await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-remote-host", "hi"), RemoteWithHostToken);

        run.Status.ShouldBe(HttpStatusCode.OK, run.Body);

        var anonymous = await host.RunAsync(
            TestAgents.Echo,
            AGUIInputs.User("t-remote-anon", "hi"),
            static request => request.Headers.Add(RemoteIpHeader, "203.0.113.9"));

        anonymous.Status.ShouldBe(HttpStatusCode.Unauthorized, "a remote caller nobody identified is still refused");
    }

    [Fact]
    public async Task With_the_three_role_policies_a_caller_identified_by_a_cookie_reaches_the_chat_endpoint_from_afar()
    {
        // No header at all: the chat endpoint's filter has to know the role policies for this, as the management API's does.
        await using var host = await AGUITestHost.StartAsync(
            TestAgents.AddEcho,
            configureServices: static services =>
            {
                AddAlwaysAuthenticated(services);
                AddRolePolicies(services);
            },
            configureEndpoints: static options => options.AllowRemoteAccess = true,
            configureApp: SimulateRemoteAddresses);

        var run = await host.RunAsync(
            TestAgents.Echo,
            AGUIInputs.User("t-remote-cookie", "hi"),
            static request => request.Headers.Add(RemoteIpHeader, "203.0.113.9"));

        run.Status.ShouldBe(HttpStatusCode.OK, run.Body);

        using var read = new HttpRequestMessage(HttpMethod.Get, new Uri("/tracon/api/runs", UriKind.Relative));
        read.Headers.Add(RemoteIpHeader, "203.0.113.9");

        using var runs = await host.Client.SendAsync(read, TestContext.Current.CancellationToken);

        runs.StatusCode.ShouldBe(HttpStatusCode.OK, "the management API answers the same caller the same way");
    }

    /// <summary>Reads the bearer value the way a JWT scheme does: a token it issued authenticates, any other value is not its business.</summary>
    private sealed class HostBearerHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();

            if (!header.StartsWith("Bearer " + HostTokenPrefix, StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "host-user")], HostScheme);

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), HostScheme)));
        }
    }
}
