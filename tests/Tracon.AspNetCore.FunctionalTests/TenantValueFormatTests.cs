using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A tenant value that is SENT and is not a well-formed tenant id is refused,
/// instead of silently becoming the default tenant.
/// </summary>
/// <remarks>
/// <para>
/// Before this, <c>HttpTenantContext</c> dropped a malformed header or claim and the <c>??</c> chain
/// fell to the default tenant, so a client whose tenant value was a typo read and wrote the default
/// tenant's data. A malformed header answers <c>400</c> (the client can correct it); a malformed
/// claim of an authenticated identity answers <c>403</c> (the client cannot).
/// </para>
/// <para>
/// HTTP-level on purpose: the behavior lives in the endpoint filter and crosses the request, the
/// authentication handler and the tenant context. The contract gate does not see it (it counts a
/// documented 4xx as documentation), so these tests are the proof.
/// </para>
/// </remarks>
public sealed class TenantValueFormatTests
{
    private const string TenantHeader = "X-Tracon-Tenant";
    private const string TenantClaim = "tenant_id";
    private const string ClaimHeader = "X-Test-Claim-Tenant";
    private const string CurrentTenant = "/tracon/api/tenants/current";

    public static TheoryData<string> MalformedValues => new()
    {
        "tenant alpha/beta",
        "tenant b/../admin",
        "    ",
        "a,b",
        new string('a', 65),
        "tenant\u00e9",
    };

    // ---- header -------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(MalformedValues))]
    public async Task A_malformed_header_is_400_and_the_value_is_not_reflected(string value)
    {
        await using var host = await StartWithHeaderAsync();

        using var response = await host.Client.SendAsync(Get(CurrentTenant, TenantHeader, value));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await AssertProblemAsync(response, "Tenant header malformed", value);
    }

    [Fact]
    public async Task An_empty_tenant_header_is_absent()
    {
        await using var host = await StartWithHeaderAsync();

        using var response = await host.Client.SendAsync(Get(CurrentTenant, TenantHeader, string.Empty));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadTenantAsync(response)).ShouldBe("default");
    }

    [Fact]
    public async Task No_header_at_all_is_the_default_tenant()
    {
        await using var host = await StartWithHeaderAsync();

        using var response = await host.Client.GetAsync(CurrentTenant, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadTenantAsync(response)).ShouldBe("default");
    }

    [Fact]
    public async Task A_well_formed_header_still_resolves_the_tenant_at_the_length_limit()
    {
        await using var host = await StartWithHeaderAsync();

        var sixtyFour = new string('a', 64);
        using var response = await host.Client.SendAsync(Get(CurrentTenant, TenantHeader, sixtyFour));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadTenantAsync(response)).ShouldBe(sixtyFour);
    }

    [Fact]
    public async Task A_malformed_header_is_400_even_with_an_allowlist()
    {
        // The allowlist check does not engage for a value that is not a tenant id, so before this the
        // request reached the default tenant even though AllowedTenants was set.
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
                options.AllowedTenants.Add("acme");
            }));

        using var malformed = await host.Client.SendAsync(Get(CurrentTenant, TenantHeader, "acme/../admin"));
        malformed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // The allowlist rule itself is unchanged: a well-formed value outside it is still 403.
        using var outside = await host.Client.SendAsync(Get(CurrentTenant, TenantHeader, "globex"));
        outside.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var inside = await host.Client.SendAsync(Get(CurrentTenant, TenantHeader, "acme"));
        inside.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_malformed_header_is_400_on_a_write_before_the_handler_runs()
    {
        await using var host = await StartWithHeaderAsync();

        using var request = new HttpRequestMessage(HttpMethod.Put, "/tracon/api/tenants/acme")
        {
            Content = JsonContent.Create(new { displayName = "Acme" }),
        };
        request.Headers.Add(TenantHeader, "bad value");

        using var response = await host.Client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Nothing was written for the default tenant on the way.
        using var list = await host.Client.GetAsync("/tracon/api/tenants", TestContext.Current.CancellationToken);
        (await list.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotContain("acme");
    }

    // ---- order against authentication --------------------------------------------

    [Fact]
    public async Task Authentication_comes_before_the_tenant_format()
    {
        // A caller who is not authenticated learns nothing about the tenant rule.
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }),
            configureEndpoints: static options => options.AuthToken = "format-test-static-token-value");

        using var unauthenticated = await host.Client.SendAsync(Get(CurrentTenant, TenantHeader, "bad value"));
        unauthenticated.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var authenticated = Get(CurrentTenant, TenantHeader, "bad value");
        authenticated.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "format-test-static-token-value");
        using var response = await host.Client.SendAsync(authenticated);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_api_key_request_with_a_malformed_header_is_400()
    {
        // The format comes before the "header must match the key's tenant" check: a value that is not
        // a tenant id is the client's mistake, not a mismatch with the key.
        await using var host = await StartWithHeaderAsync();

        var key = await CreateKeyAsync(host, "acme");

        using var malformed = WithKey(Get(CurrentTenant, TenantHeader, "bad value"), key);
        using var malformedResponse = await host.Client.SendAsync(malformed);
        malformedResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // The existing rules around it are unchanged.
        using var mismatch = WithKey(Get(CurrentTenant, TenantHeader, "globex"), key);
        using var mismatchResponse = await host.Client.SendAsync(mismatch);
        mismatchResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var matching = WithKey(Get(CurrentTenant, TenantHeader, "acme"), key);
        using var matchingResponse = await host.Client.SendAsync(matching);
        matchingResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- claim --------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(MalformedValues))]
    public async Task A_malformed_claim_is_403_and_the_value_is_not_reflected(string value)
    {
        await using var host = await StartWithClaimAsync();

        using var response = await host.Client.SendAsync(Get(CurrentTenant, ClaimHeader, value));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await AssertProblemAsync(response, "Tenant claim malformed", value);
    }

    [Fact]
    public async Task A_missing_claim_still_resolves_the_default_tenant()
    {
        // A missing value is not a malformed one. That is a separate question this change does not change.
        await using var host = await StartWithClaimAsync();

        using var response = await host.Client.GetAsync(CurrentTenant, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadTenantAsync(response)).ShouldBe("default");
    }

    [Fact]
    public async Task A_well_formed_claim_resolves_the_tenant()
    {
        await using var host = await StartWithClaimAsync();

        using var response = await host.Client.SendAsync(Get(CurrentTenant, ClaimHeader, "Acme"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadTenantAsync(response)).ShouldBe("acme");
    }

    [Fact]
    public async Task With_a_claim_type_the_header_is_not_read()
    {
        // The header is never read while a claim type is set, so a malformed header cannot be a 400.
        await using var host = await StartWithClaimAsync(allowHeader: true);

        using var response = await host.Client.SendAsync(Get(CurrentTenant, TenantHeader, "bad value"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadTenantAsync(response)).ShouldBe("default");
    }

    // ---- the switches that keep it off ---------------------------------------------

    [Fact]
    public async Task With_the_header_path_closed_a_malformed_header_changes_nothing()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.UseTenancy(static options => options.Enabled = true));

        using var response = await host.Client.SendAsync(Get(CurrentTenant, TenantHeader, "bad value"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadTenantAsync(response)).ShouldBe("default");
    }

    [Fact]
    public async Task With_tenancy_off_the_header_changes_nothing()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.SendAsync(Get(CurrentTenant, TenantHeader, "bad value"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadTenantAsync(response)).ShouldBe("default");
    }

    [Fact]
    public async Task With_UseTenancy_disabled_the_header_changes_nothing()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = false;
                options.AllowHeaderResolution = true;
            }));

        using var response = await host.Client.SendAsync(Get(CurrentTenant, TenantHeader, "bad value"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- other surfaces ------------------------------------------------------------

    [Theory]
    [InlineData("/tracon/api/agents")]
    [InlineData("/tracon/api/sessions")]
    [InlineData("/tracon/api/workflows")]
    public async Task A_malformed_header_is_400_across_the_protected_group(string path)
    {
        await using var host = await StartWithHeaderAsync();

        using var response = await host.Client.SendAsync(Get(path, TenantHeader, "bad value"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_malformed_header_is_400_on_the_a2a_surface()
    {
        // The A2A group builds its own filter instance: a check that lives only on the main group would
        // leave this surface reading the default tenant.
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder
                .UseA2A(static options => options.ExposedAgents.Add("support"))
                .UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                }),
            configureAfterMap: static app => app.MapTraconA2A());

        using var response = await host.Client.SendAsync(
            Get("/tracon/a2a/support/.well-known/agent-card.json", TenantHeader, "bad value"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_malformed_header_is_400_on_a_group_that_does_not_apply_the_bearer_layer(bool presentStaticToken)
    {
        // The inbound trigger group is built with requireBearerToken: false, so its filter takes the
        // "static token presented, but this group does not apply the bearer layer" branch when the
        // installation's token is sent, and the anonymous branch when nothing is. Both are separate
        // code paths from the main group's, and each reaches the tenant check on its own.
        const string Token = "group-branch-static-token-value";

        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }),
            configureEndpoints: static options => options.AuthToken = Token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/tracon/api/triggers/acme/no-such-trigger")
        {
            Content = JsonContent.Create(new { }),
        };
        request.Headers.TryAddWithoutValidation(TenantHeader, "bad value");

        if (presentStaticToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        }

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await AssertProblemAsync(response, "Tenant header malformed", "bad value");
    }

    [Fact]
    public async Task A_malformed_header_is_400_on_the_mcp_server_surface()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder
                .UseMcpServer()
                .UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                }),
            configureAfterMap: static app => app.MapTraconMcpServer());

        // The MCP transport answers a POST (a JSON-RPC message); a GET to the same path is 405 before
        // any filter runs, which would say nothing about the tenant check.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/tracon/mcp")
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"ping"}""", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        request.Headers.TryAddWithoutValidation(TenantHeader, "bad value");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---- a value that ends in a line feed --------------------------
    // .NET's `$` matches before a trailing '\n' as well as at the very end, so a pattern written
    // `^...$` accepted "acme\n". A header cannot carry the character (the transport forbids it), so the
    // two places it can arrive are a claim an identity provider issued and a route segment.

    [Theory]
    [InlineData("acme\n")]
    [InlineData("acme\r\n")]
    [InlineData("a\n")]
    public void A_tenant_id_that_ends_in_a_line_break_is_not_well_formed(string value)
    {
        HttpTenantContext.IsValidTenantId(value).ShouldBeFalse();
    }

    [Fact]
    public void A_tenant_id_without_the_line_break_is_still_well_formed()
    {
        HttpTenantContext.IsValidTenantId("acme").ShouldBeTrue();
        HttpTenantContext.IsValidTenantId("Acme.Inc_1-x").ShouldBeTrue();
    }

    [Fact]
    public async Task A_claim_that_ends_in_a_line_feed_is_403_and_is_not_the_acme_tenant()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.ClaimType = TenantClaim;
                options.AllowHeaderResolution = false;
            }),
            configureServices: static services => services
                .AddAuthentication(EncodedClaimHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, EncodedClaimHandler>(
                    EncodedClaimHandler.SchemeName,
                    configureOptions: null));

        using var response = await host.Client.SendAsync(Get(CurrentTenant, EncodedClaimHandler.Header, "acme%0A"));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await AssertProblemAsync(response, "Tenant claim malformed", "acme\n");

        // The same claim without the trailing character still resolves, so the test above fails for the
        // line feed and not for the handler.
        using var clean = await host.Client.SendAsync(Get(CurrentTenant, EncodedClaimHandler.Header, "acme"));

        clean.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadTenantAsync(clean)).ShouldBe("acme");
    }

    [Fact]
    public async Task A_route_tenant_that_ends_in_a_line_feed_is_400_on_the_egress_endpoint()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri("/tracon/api/tenants/acme%0A/egress", UriKind.Relative))
        {
            Content = JsonContent.Create(new { allowedProviders = new[] { "openai" } }),
        };

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Nothing was saved under a name no request can ever resolve to.
        using var read = await host.Client.GetAsync(
            new Uri("/tracon/api/tenants/acme/egress", UriKind.Relative),
            TestContext.Current.CancellationToken);

        (await read.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotContain("openai");
    }

    [Fact]
    public async Task A_route_tenant_that_ends_in_a_line_feed_is_400_when_a_tenant_record_is_created()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri("/tracon/api/tenants/acme%0A", UriKind.Relative))
        {
            Content = JsonContent.Create(new { displayName = "Acme" }),
        };

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---- helpers -------------------------------------------------------------------

    private static Task<TraconTestHost> StartWithHeaderAsync()
        => TraconTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

    private static Task<TraconTestHost> StartWithClaimAsync(bool allowHeader = false)
        => TraconTestHost.StartAsync(
            builder => builder.UseTenancy(options =>
            {
                options.Enabled = true;
                options.ClaimType = TenantClaim;
                options.AllowHeaderResolution = allowHeader;
            }),
            configureServices: static services => services
                .AddAuthentication(ClaimFromHeaderHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, ClaimFromHeaderHandler>(
                    ClaimFromHeaderHandler.SchemeName,
                    configureOptions: null));

    private static HttpRequestMessage Get(string path, string header, string value)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.TryAddWithoutValidation(header, value);

        return request;
    }

    private static HttpRequestMessage WithKey(HttpRequestMessage request, string key)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        return request;
    }

    private static async Task<string> CreateKeyAsync(TraconTestHost host, string tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/tracon/api/api-keys")
        {
            Content = JsonContent.Create(new { name = $"{tenant}-key", scopes = new[] { "SecurityAdmin" } }),
        };
        request.Headers.Add(TenantHeader, tenant);

        using var response = await host.Client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<ApiKeyCreationResult>();

        return created.ShouldNotBeNull().PlaintextKey;
    }

    private static async Task<string> ReadTenantAsync(HttpResponseMessage response)
    {
        var current = await response.Content.ReadFromJsonAsync<CurrentTenantResponse>();

        return current.ShouldNotBeNull().TenantId;
    }

    /// <summary>Checks the problem title and that the client's text is nowhere in the body.</summary>
    private static async Task AssertProblemAsync(HttpResponseMessage response, string title, string sentValue)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(body);

        document.RootElement.GetProperty("title").GetString().ShouldBe(title);

        // The value is the client's text; an echo would carry it into logs and the interface.
        if (sentValue.Trim().Length > 0)
        {
            body.ShouldNotContain(sentValue);
        }
    }

    /// <summary>
    /// Authenticates every request; the tenant claim is the PERCENT-DECODED value of a test header, so a test
    /// can issue a claim that holds a character a header value cannot (a line feed).
    /// </summary>
    private sealed class EncodedClaimHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "EncodedClaim";
        public const string Header = "X-Test-Claim-Tenant-Encoded";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> { new(ClaimTypes.Name, "claim-user") };

            if (Request.Headers.TryGetValue(Header, out var tenant))
            {
                claims.Add(new Claim(TenantClaim, Uri.UnescapeDataString(tenant.ToString())));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }

    /// <summary>Authenticates every request; the tenant claim is the value of a test header.</summary>
    private sealed class ClaimFromHeaderHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "ClaimFromHeader";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> { new(ClaimTypes.Name, "claim-user") };

            if (Request.Headers.TryGetValue(ClaimHeader, out var tenant))
            {
                claims.Add(new Claim(TenantClaim, tenant.ToString()));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
