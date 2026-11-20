using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// With the three role policies registered and no general policy, a remote caller who passes its role policy is let in; a
/// caller no policy asked is not.
/// </summary>
/// <remarks>
/// <para>
/// The anonymous check used to look only at the general <c>RequireAuthorization</c> policy. A cookie or token that the
/// host's authentication reads is not an <c>Authorization</c> header, so an admin who passed the role policy was still
/// taken for an anonymous caller and refused as a remote one. The role policy had already decided about them.
/// </para>
/// <para>
/// The claim runs in two directions and both are pinned here. A request that passed a role policy is not refused as
/// anonymous, and a request that no policy asked (one role registered, the others not; remote access off; no credential)
/// keeps the refusal it had.
/// </para>
/// </remarks>
public sealed class RolePolicyRemoteAccessTests
{
    private const string RemoteAddress = "203.0.113.5";
    private const string AgentsPath = "/tracon/api/agents";

    [Fact]
    public async Task A_remote_caller_who_passes_the_role_policy_is_let_in_without_a_general_policy()
    {
        await using var host = await StartAsync(RolePolicies.All);

        using var response = await host.Client.SendAsync(Get(AgentsPath, role: "reader"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_remote_caller_who_fails_the_role_policy_is_refused_by_the_policy()
    {
        await using var host = await StartAsync(RolePolicies.All);

        using var wrongRole = await host.Client.SendAsync(Get(AgentsPath, role: "intern"));
        using var anonymous = await host.Client.SendAsync(Get(AgentsPath, role: null));

        wrongRole.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task One_registered_role_policy_is_not_an_authentication_method()
    {
        // The routes of the other roles carry no policy, so the refusal of an anonymous remote request has to stay.
        await using var host = await StartAsync(RolePolicies.ReaderOnly);

        using var response = await host.Client.SendAsync(Get(AgentsPath, role: "reader"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).ShouldContain(nameof(TraconEndpointOptions.RequireAuthorization));
    }

    [Fact]
    public async Task Remote_access_off_still_refuses_a_remote_caller_who_passes_the_role_policy()
    {
        await using var host = await StartAsync(RolePolicies.All, allowRemoteAccess: false);

        using var response = await host.Client.SendAsync(Get(AgentsPath, role: "reader"));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Three_policies_that_let_everyone_through_do_not_make_an_anonymous_remote_caller_a_known_one()
    {
        // The registration is not the proof: the guard asks the request whether its caller was identified.
        await using var host = await StartAsync(RolePolicies.AllPermissive);

        using var response = await host.Client.SendAsync(Get(AgentsPath, role: null));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("http://attacker.example/tracon/api/agents", null)]
    [InlineData("http://localhost/tracon/api/agents", "https://attacker.example")]
    public async Task A_request_from_this_machine_keeps_the_host_and_origin_defence_when_the_role_policies_are_registered(string uri, string? origin)
    {
        // Loosening is for the remote branch only: a page of another site reaching a local service through DNS rebinding
        // or a cross-site request is refused as before, whoever the host's authentication says the user is.
        await using var host = await StartAsync(RolePolicies.All, allowRemoteAccess: false);

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(uri));
        request.Headers.Add(EndpointAuthorizationMatrixTests.RoleHeader, "reader");

        if (origin is not null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_refusal_names_the_role_policies_as_a_way_out()
    {
        await using var host = await StartAsync(RolePolicies.None);

        using var response = await host.Client.SendAsync(Get(AgentsPath, role: null));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).ShouldContain("role policies");
    }

    private enum RolePolicies
    {
        None,
        ReaderOnly,
        All,
        AllPermissive,
    }

    private static Task<TraconTestHost> StartAsync(RolePolicies policies, bool allowRemoteAccess = true)
        => TraconTestHost.StartAsync(
            configureEndpoints: options => options.AllowRemoteAccess = allowRemoteAccess,
            configureServices: services =>
            {
                services
                    .AddAuthentication(EndpointAuthorizationMatrixTests.RoleHeaderAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, EndpointAuthorizationMatrixTests.RoleHeaderAuthenticationHandler>(
                        EndpointAuthorizationMatrixTests.RoleHeaderAuthenticationHandler.SchemeName,
                        configureOptions: null);

                var authorization = services.AddAuthorizationBuilder();

                if (policies is RolePolicies.ReaderOnly or RolePolicies.All)
                {
                    authorization.AddPolicy(TraconPolicies.Reader, static policy => policy.RequireRole("reader"));
                }

                if (policies == RolePolicies.All)
                {
                    authorization.AddPolicy(TraconPolicies.Operator, static policy => policy.RequireRole("operator"));
                    authorization.AddPolicy(TraconPolicies.Admin, static policy => policy.RequireRole("admin"));
                }

                if (policies == RolePolicies.AllPermissive)
                {
                    authorization.AddPolicy(TraconPolicies.Reader, static policy => policy.RequireAssertion(static _ => true));
                    authorization.AddPolicy(TraconPolicies.Operator, static policy => policy.RequireAssertion(static _ => true));
                    authorization.AddPolicy(TraconPolicies.Admin, static policy => policy.RequireAssertion(static _ => true));
                }
            });

    private static HttpRequestMessage Get(string path, string? role)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));

        request.Headers.Add(TraconTestHost.RemoteIpHeader, RemoteAddress);

        if (role is null)
        {
            request.Headers.Add(EndpointAuthorizationMatrixTests.AnonymousHeader, "1");
        }
        else
        {
            request.Headers.Add(EndpointAuthorizationMatrixTests.RoleHeader, role);
        }

        return request;
    }
}
