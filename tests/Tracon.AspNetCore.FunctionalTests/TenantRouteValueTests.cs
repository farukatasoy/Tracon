using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A tenant that arrives in a route segment is judged before any store sees it: a value outside the tenant
/// alphabet answers <c>400</c>, never <c>500</c>, and nothing in the answer repeats it.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 Every store refuses a tenant identifier with a character other than an ASCII letter, a digit, <c>_</c>,
/// <c>.</c> or <c>-</c> (an exception, since a relational engine could join two other spellings into one tenant).
/// A route segment is the client's text, and the management routes took it straight to a store: a GET or a DELETE
/// with a non-ASCII tenant reached the store before anything judged it. The check sits in front of the
/// authority check and the handler of every route that carries a tenant segment.
/// </para>
/// <para>
/// The routes are read from the endpoint table, so a route added tomorrow joins the sweep without a line here.
/// </para>
/// </remarks>
public sealed class TenantRouteValueTests
{
    /// <summary>Percent-encoded UTF-8 of a composed accented letter, a decomposed one, a full-width letter, a space and a colon.</summary>
    public static TheoryData<string> MalformedSegments => new()
    {
        "caf%C3%A9",
        "cafe%CC%81",
        "%EF%BD%81cme",
        "ac%20me",
        "acme%3Aops",
    };

    [Fact]
    public async Task Every_route_with_a_tenant_segment_carries_the_validation()
    {
        await using var host = await StartAsync();

        var routes = TenantRoutes(host);

        // A sweep that found nothing would pass for the wrong reason: eleven management routes carry one today.
        routes.Count.ShouldBeGreaterThanOrEqualTo(11);

        var unguarded = routes
            .Where(static route => !route.Endpoint.Metadata.Any(static item => string.Equals(item.GetType().Name, "TenantRouteValidationMetadata", StringComparison.Ordinal)))
            .Select(static route => route.Describe())
            .ToList();

        unguarded.ShouldBeEmpty("a route that names a tenant has to judge the segment before its handler runs");
    }

    [Theory]
    [MemberData(nameof(MalformedSegments))]
    public async Task A_malformed_tenant_segment_answers_400_on_every_management_route(string segment)
    {
        await using var host = await StartAsync();

        var failures = new List<string>();

        foreach (var route in TenantRoutes(host))
        {
            using var request = new HttpRequestMessage(route.Method, new Uri(route.Fill(segment), UriKind.Relative));

            if (route.Method != HttpMethod.Get && route.Method != HttpMethod.Delete)
            {
                request.Content = JsonContent.Create(new { });
            }

            using var response = await host.Client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (response.StatusCode != HttpStatusCode.BadRequest)
            {
                failures.Add($"{route.Describe()} -> {(int)response.StatusCode}");
            }
            else if (body.Contains("caf", StringComparison.OrdinalIgnoreCase) || body.Contains("acme", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"{route.Describe()} repeats the value in its answer");
            }
        }

        failures.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_well_formed_tenant_segment_still_reaches_the_handler()
    {
        // The other direction: a check that refused every tenant would satisfy the sweep above.
        await using var host = await StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/tenants/Acme-1.Prod_2/egress", UriKind.Relative));

        response.StatusCode.ShouldNotBe(HttpStatusCode.BadRequest);
        ((int)response.StatusCode).ShouldBeLessThan(500);
    }

    [Fact]
    public async Task A_custom_context_that_returns_a_non_ascii_tenant_fails_on_the_first_store_call()
    {
        // The context is the consumer's code, and a tenant it returns outside the alphabet is its defect: the
        // request must not run as some other tenant, and must not be answered with someone else's data.
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => services.AddSingleton<ITenantContext>(new RawTenantContext("caf" + (char)0x00E9)));

        HttpResponseMessage? response = null;

        try
        {
            response = await host.Client.GetAsync(new Uri("/tracon/api/runs", UriKind.Relative));
        }
        catch (ArgumentException failure)
        {
            failure.ParamName.ShouldBe("tenantId");

            return;
        }

        using (response)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        }
    }

    [Fact]
    public async Task A_default_tenant_outside_the_alphabet_stops_the_host_at_startup()
    {
        var failure = await Should.ThrowAsync<OptionsValidationException>(
            async () =>
            {
                await using var host = await TraconTestHost.StartAsync(
                    configureServices: static services => services.Configure<TraconOptions>(static options => options.DefaultTenantId = "caf" + (char)0x00E9));
            });

        failure.Message.ShouldContain("DefaultTenantId");
    }

    [Fact]
    public async Task An_allowed_tenant_outside_the_alphabet_stops_the_host_at_startup()
    {
        var failure = await Should.ThrowAsync<OptionsValidationException>(
            async () =>
            {
                await using var host = await TraconTestHost.StartAsync(
                    static builder => builder.UseTenancy(static options =>
                    {
                        options.Enabled = true;
                        options.AllowHeaderResolution = true;
                        options.AllowedTenants.Add("acme");
                        options.AllowedTenants.Add("caf" + (char)0x00E9);
                    }));
            });

        failure.Message.ShouldContain("AllowedTenants");
    }

    [Fact]
    public async Task An_allowed_tenant_longer_than_a_request_can_carry_stops_the_host_at_startup()
    {
        var failure = await Should.ThrowAsync<OptionsValidationException>(
            async () =>
            {
                await using var host = await TraconTestHost.StartAsync(
                    static builder => builder.UseTenancy(static options =>
                    {
                        options.Enabled = true;
                        options.AllowHeaderResolution = true;
                        options.AllowedTenants.Add(new string('a', 65));
                    }));
            });

        failure.Message.ShouldContain("AllowedTenants");
        failure.Message.ShouldContain("64");
    }

    private static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(static builder => builder.UseTenancy(static options =>
        {
            options.Enabled = true;
            options.AllowHeaderResolution = true;
        }));

    private static List<TenantRoute> TenantRoutes(TraconTestHost host)
        => host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(static endpoint => endpoint.RoutePattern.RawText is { } text
                && (text.Contains("{tenantId}", StringComparison.Ordinal) || text.Contains("{slug}", StringComparison.Ordinal))
                && !text.Contains("/triggers/", StringComparison.Ordinal))
            .SelectMany(static endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods
                .Select(method => new TenantRoute(endpoint, new HttpMethod(method))))
            .ToList();

    private sealed class RawTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId { get; } = tenantId;
    }

    private sealed record TenantRoute(RouteEndpoint Endpoint, HttpMethod Method)
    {
        /// <summary>Fills the tenant segment with <paramref name="segment"/> and every other parameter with a plausible value; the pattern already carries the prefix of the group.</summary>
        public string Fill(string segment)
        {
            var path = Endpoint.RoutePattern.RawText!
                .Replace("{tenantId}", segment, StringComparison.Ordinal)
                .Replace("{slug}", segment, StringComparison.Ordinal);

            foreach (var parameter in Endpoint.RoutePattern.Parameters)
            {
                path = path.Replace("{" + parameter.Name + "}", "openai", StringComparison.Ordinal);
            }

            return path;
        }

        public string Describe() => $"{Method} {Endpoint.RoutePattern.RawText}";
    }
}
