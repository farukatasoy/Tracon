using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A host's own bearer token is judged by the host's rules, and it never stands in for a credential Tracon requires.
/// </summary>
/// <remarks>
/// The token is the host's only when its authentication identified the caller. What Tracon still requires stays required: a
/// surface published beyond loopback that takes API keys only asks for a key, and a role policy that denies the caller
/// answers the way it answers a caller signed in with a cookie.
/// </remarks>
public sealed class HostCredentialSurfaceTests
{
    private const string Scheme = "HostBearer";
    private const string HostToken = "host-jwt-7c1e90b2";

    [Fact]
    public async Task A_surface_that_requires_a_key_asks_an_identified_caller_for_one()
    {
        string key = null!;

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(static options => options.ExposedAgents.Add("kod-agent")),
            configureEndpoints: static options => options.AllowRemoteAccess = true,
            configureServices: static services => AddHostBearer(services, operators: true),
            configureAfterMap: app =>
            {
                key = app.Services.GetRequiredService<IApiKeyStore>()
                    .CreateAsync(new ApiKeyDraft { TenantId = "default", Name = "external", Scopes = [ApiKeyScope.ExternalInvoke] })
                    .AsTask().GetAwaiter().GetResult().PlaintextKey;
                app.MapTraconMcpServer();
            });

        var (refused, _) = await McpTestClient.SendAsync(host.Client, "/tracon/mcp", "tools/list", token: HostToken);
        var (accepted, _) = await McpTestClient.SendAsync(host.Client, "/tracon/mcp", "tools/list", token: key);

        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "the host's token does not stand in for the key this surface requires");
        (await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("API key required", Case.Sensitive);
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_role_policy_that_denies_the_caller_still_answers_403()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => AddHostBearer(services, operators: false));

        using var read = new HttpRequestMessage(HttpMethod.Get, new Uri("/tracon/api/runs", UriKind.Relative));
        read.Headers.Authorization = new AuthenticationHeaderValue("Bearer", HostToken);

        using var readable = await host.Client.SendAsync(read, TestContext.Current.CancellationToken);

        using var write = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/runs/{Guid.NewGuid()}/cancel", UriKind.Relative));
        write.Headers.Authorization = new AuthenticationHeaderValue("Bearer", HostToken);

        using var denied = await host.Client.SendAsync(write, TestContext.Current.CancellationToken);

        readable.StatusCode.ShouldBe(HttpStatusCode.OK, "the reader policy admits the caller");
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden, "the operator policy denies it, whatever carried the identity");
    }

    private static void AddHostBearer(IServiceCollection services, bool operators)
    {
        services
            .AddAuthentication(Scheme)
            .AddScheme<AuthenticationSchemeOptions, HostBearerHandler>(Scheme, configureOptions: null);

        services.AddAuthorizationBuilder()
            .AddPolicy(TraconPolicies.Reader, static policy => policy.RequireAuthenticatedUser())
            .AddPolicy(TraconPolicies.Operator, policy => policy.RequireAssertion(_ => operators))
            .AddPolicy(TraconPolicies.Admin, static policy => policy.RequireAssertion(static _ => false));
    }

    /// <summary>Authenticates the one token it issued, the way a JWT scheme does, and says nothing about any other value.</summary>
    private sealed class HostBearerHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!string.Equals(Request.Headers.Authorization.ToString(), "Bearer " + HostToken, StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "host-user")], HostCredentialSurfaceTests.Scheme);

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), HostCredentialSurfaceTests.Scheme)));
        }
    }
}
