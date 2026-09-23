using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The endpoints that take the tenant from the REQUEST instead of the
/// caller — <c>/api/tenants/{tenantId}/providers</c>, <c>/egress</c> and the
/// tenant records — act on another tenant only for a caller with platform
/// authority.
/// </summary>
/// <remarks>
/// <para>
/// Before this rule, a key bound to tenant A with <c>SecurityAdmin</c>, or a
/// claims-based Admin of tenant A, could point tenant B's provider binding at
/// an address A controls: every model call of B then sent B's prompts and its
/// resolved key there. K-469 accepted the route tenant for a "platform
/// admin", but nothing enforced that the caller WAS one.
/// </para>
/// <para>
/// Platform authority per identity: an API key needs the
/// <c>PlatformAdmin</c> scope; the static <c>AuthToken</c> identifies the
/// installation; a claims principal needs the <c>Tracon.PlatformAdmin</c>
/// policy, and a missing policy denies; an anonymous loopback caller is the
/// zero-configuration local operator (K1).
/// </para>
/// </remarks>
public sealed class CrossTenantAuthorityTests
{
    private const string TenantHeader = "X-Tracon-Tenant";
    private const string StaticToken = "cross-tenant-static-token-value";

    private static Task<TraconTestHost> StartWithHeaderTenancyAsync(Action<TraconEndpointOptions>? configureEndpoints = null)
        => TraconTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }),
            configureEndpoints);

    private static Task<TraconTestHost> StartWithClaimsTenancyAsync(bool registerPlatformPolicy)
        => TraconTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.ClaimType = ClaimsFromHeadersHandler.TenantClaim;
            }),
            configureServices: services =>
            {
                services
                    .AddAuthentication(ClaimsFromHeadersHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, ClaimsFromHeadersHandler>(
                        ClaimsFromHeadersHandler.SchemeName,
                        configureOptions: null);

                var authorization = services.AddAuthorizationBuilder();

                if (registerPlatformPolicy)
                {
                    authorization.AddPolicy(
                        TraconPolicies.PlatformAdmin,
                        static policy => policy.RequireClaim(ClaimsFromHeadersHandler.PlatformClaim, "yes"));
                }
            });

    private static async Task<string> CreateKeyAsync(TraconTestHost host, string tenant, params string[] scopes)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/tracon/api/api-keys")
        {
            Content = JsonContent.Create(new { name = $"{tenant}-key", scopes }),
        };
        request.Headers.Add(TenantHeader, tenant);

        using var response = await host.Client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<ApiKeyCreationResult>();

        return created.ShouldNotBeNull().PlaintextKey;
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, object? body = null)
        => new(method, new Uri(path, UriKind.Relative))
        {
            Content = body is null ? null : JsonContent.Create(body),
        };

    private static HttpRequestMessage SaveBinding(string tenant)
        => Request(
            HttpMethod.Put,
            $"/tracon/api/tenants/{tenant}/providers/openai",
            new { apiKeyConfigurationName = $"Tracon:ProviderKeys:{tenant}:OpenAI" });

    private static HttpRequestMessage WithKey(HttpRequestMessage request, string key)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        return request;
    }

    private static HttpRequestMessage AsClaimsUser(HttpRequestMessage request, string tenant, bool platform = false)
    {
        request.Headers.Add(ClaimsFromHeadersHandler.TenantHeader, tenant);

        if (platform)
        {
            request.Headers.Add(ClaimsFromHeadersHandler.PlatformHeader, "yes");
        }

        return request;
    }

    // ---- API key ------------------------------------------------------------------

    public static TheoryData<string, string> OtherTenantEndpoints => new()
    {
        { "GET", "/tracon/api/tenants/globex/providers" },
        { "DELETE", "/tracon/api/tenants/globex/providers/openai" },
        { "GET", "/tracon/api/tenants/globex/egress" },
        { "DELETE", "/tracon/api/tenants/globex/egress" },
    };

    [Fact]
    public async Task Tenant_bound_key_cannot_write_another_tenants_provider_binding()
    {
        await using var host = await StartWithHeaderTenancyAsync();
        var key = await CreateKeyAsync(host, "acme", "SecurityAdmin");

        using var request = WithKey(SaveBinding("globex"), key);
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).ShouldContain("PlatformAdmin");
    }

    [Fact]
    public async Task Tenant_bound_key_cannot_write_another_tenants_egress_policy()
    {
        await using var host = await StartWithHeaderTenancyAsync();
        var key = await CreateKeyAsync(host, "acme", "SecurityAdmin");

        using var request = WithKey(
            Request(HttpMethod.Put, "/tracon/api/tenants/globex/egress", new { allowedProviders = new[] { "openai" } }),
            key);
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(OtherTenantEndpoints))]
    public async Task Tenant_bound_key_cannot_read_or_delete_another_tenants_settings(string method, string path)
    {
        await using var host = await StartWithHeaderTenancyAsync();
        var key = await CreateKeyAsync(host, "acme", "SecurityAdmin");

        using var request = WithKey(Request(new HttpMethod(method), path), key);
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Tenant_bound_key_manages_its_own_tenant_with_security_admin_alone()
    {
        await using var host = await StartWithHeaderTenancyAsync();
        var key = await CreateKeyAsync(host, "acme", "SecurityAdmin");

        using var request = WithKey(SaveBinding("acme"), key);
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>The route tenant is folded like every other tenant id: 'Acme' is the caller's own.</summary>
    [Fact]
    public async Task Route_tenant_is_compared_in_canonical_form()
    {
        await using var host = await StartWithHeaderTenancyAsync();
        var key = await CreateKeyAsync(host, "acme", "SecurityAdmin");

        using var request = WithKey(Request(HttpMethod.Get, "/tracon/api/tenants/Acme/providers"), key);
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Key_with_platform_admin_scope_manages_another_tenant()
    {
        await using var host = await StartWithHeaderTenancyAsync();
        var key = await CreateKeyAsync(host, "acme", "SecurityAdmin", "PlatformAdmin");

        using var request = WithKey(SaveBinding("globex"), key);
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- Static token and anonymous loopback --------------------------------------

    [Fact]
    public async Task Static_token_identifies_the_installation_and_manages_any_tenant()
    {
        await using var host = await StartWithHeaderTenancyAsync(static options => options.AuthToken = StaticToken);

        using var request = WithKey(SaveBinding("globex"), StaticToken);
        request.Headers.Add(TenantHeader, "acme");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Anonymous_loopback_operator_keeps_zero_configuration_access()
    {
        await using var host = await StartWithHeaderTenancyAsync();

        using var request = SaveBinding("globex");
        request.Headers.Add(TenantHeader, "acme");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- Claims principal ---------------------------------------------------------

    /// <summary>
    /// 🚨 A missing policy DENIES. The role policies fall back to "allowed"
    /// when unregistered (K1); a cross-tenant write must not.
    /// </summary>
    [Fact]
    public async Task Claims_user_without_a_registered_platform_policy_is_denied()
    {
        await using var host = await StartWithClaimsTenancyAsync(registerPlatformPolicy: false);

        using var request = AsClaimsUser(SaveBinding("globex"), "acme", platform: true);
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).ShouldContain(TraconPolicies.PlatformAdmin);
    }

    [Fact]
    public async Task Claims_user_that_fails_the_platform_policy_is_denied()
    {
        await using var host = await StartWithClaimsTenancyAsync(registerPlatformPolicy: true);

        using var request = AsClaimsUser(SaveBinding("globex"), "acme");
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Claims_user_that_passes_the_platform_policy_manages_another_tenant()
    {
        await using var host = await StartWithClaimsTenancyAsync(registerPlatformPolicy: true);

        using var request = AsClaimsUser(SaveBinding("globex"), "acme", platform: true);
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Claims_user_manages_its_own_tenant_without_the_platform_policy()
    {
        await using var host = await StartWithClaimsTenancyAsync(registerPlatformPolicy: false);

        using var request = AsClaimsUser(SaveBinding("acme"), "acme");
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- Tenant records (same class: the tenant comes from the request) -----------

    [Fact]
    public async Task Claims_user_without_platform_authority_cannot_list_every_tenant()
    {
        await using var host = await StartWithClaimsTenancyAsync(registerPlatformPolicy: true);

        using var request = AsClaimsUser(Request(HttpMethod.Get, "/tracon/api/tenants"), "acme");
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Claims_user_without_platform_authority_cannot_rename_another_tenant()
    {
        await using var host = await StartWithClaimsTenancyAsync(registerPlatformPolicy: true);

        using var request = AsClaimsUser(
            Request(HttpMethod.Put, "/tracon/api/tenants/globex", new { displayName = "Hijacked" }),
            "acme");
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Claims_user_without_platform_authority_cannot_delete_another_tenant()
    {
        await using var host = await StartWithClaimsTenancyAsync(registerPlatformPolicy: true);

        using var request = AsClaimsUser(Request(HttpMethod.Delete, "/tracon/api/tenants/globex"), "acme");
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// With multi-tenancy off a claim resolves no tenant, so there is no other
    /// tenant to reach: a single-tenant installation keeps working unchanged.
    /// </summary>
    [Fact]
    public async Task Claims_user_of_a_single_tenant_installation_is_unaffected()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => services
                .AddAuthentication(ClaimsFromHeadersHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, ClaimsFromHeadersHandler>(
                    ClaimsFromHeadersHandler.SchemeName,
                    configureOptions: null));

        using var list = AsClaimsUser(Request(HttpMethod.Get, "/tracon/api/tenants"), "acme");
        using var listed = await host.Client.SendAsync(list);

        using var save = AsClaimsUser(SaveBinding("globex"), "acme");
        using var saved = await host.Client.SendAsync(save);

        listed.StatusCode.ShouldBe(HttpStatusCode.OK);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Claims_user_with_platform_authority_lists_every_tenant()
    {
        await using var host = await StartWithClaimsTenancyAsync(registerPlatformPolicy: true);

        using var request = AsClaimsUser(Request(HttpMethod.Get, "/tracon/api/tenants"), "acme", platform: true);
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- Minting a key that carries platform authority -------------------------

    private static HttpRequestMessage CreateKeyRequest(params string[] scopes)
        => Request(HttpMethod.Post, "/tracon/api/api-keys", new { name = "minted", scopes });

    /// <summary>
    /// 🚨 A key that carries PlatformAdmin reaches every tenant. If a
    /// claims-based Admin of one tenant could create one, the policy
    /// requirement above would be one request away from meaningless.
    /// </summary>
    [Fact]
    public async Task Claims_user_without_platform_authority_cannot_mint_a_platform_key()
    {
        await using var host = await StartWithClaimsTenancyAsync(registerPlatformPolicy: false);

        using var request = AsClaimsUser(CreateKeyRequest("SecurityAdmin", "PlatformAdmin"), "acme");
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).ShouldContain(TraconPolicies.PlatformAdmin);
    }

    [Fact]
    public async Task Claims_user_that_passes_the_platform_policy_mints_a_platform_key()
    {
        await using var host = await StartWithClaimsTenancyAsync(registerPlatformPolicy: true);

        using var request = AsClaimsUser(CreateKeyRequest("SecurityAdmin", "PlatformAdmin"), "acme", platform: true);
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Claims_user_still_mints_a_key_for_its_own_tenant_without_platform_scope()
    {
        await using var host = await StartWithClaimsTenancyAsync(registerPlatformPolicy: false);

        using var request = AsClaimsUser(CreateKeyRequest("SecurityAdmin", "AgentsAdmin"), "acme");
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Static_token_mints_a_platform_key()
    {
        await using var host = await StartWithHeaderTenancyAsync(static options => options.AuthToken = StaticToken);

        using var request = WithKey(CreateKeyRequest("PlatformAdmin"), StaticToken);
        request.Headers.Add(TenantHeader, "acme");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Platform_key_mints_another_platform_key()
    {
        await using var host = await StartWithHeaderTenancyAsync();
        var key = await CreateKeyAsync(host, "acme", "SecurityAdmin", "PlatformAdmin");

        using var request = WithKey(CreateKeyRequest("PlatformAdmin"), key);
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// Authenticates every request; the tenant and the platform marker come
    /// from test headers, so one host can play several callers.
    /// </summary>
    private sealed class ClaimsFromHeadersHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "ClaimsFromHeaders";
        public const string TenantClaim = "tracon_tenant";
        public const string PlatformClaim = "tracon_platform";
        public const string TenantHeader = "X-Test-Claim-Tenant";
        public const string PlatformHeader = "X-Test-Claim-Platform";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> { new(ClaimTypes.Name, "claims-user") };

            if (Request.Headers[TenantHeader].ToString() is { Length: > 0 } tenant)
            {
                claims.Add(new Claim(TenantClaim, tenant));
            }

            if (Request.Headers[PlatformHeader].ToString() is { Length: > 0 } platform)
            {
                claims.Add(new Claim(PlatformClaim, platform));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
