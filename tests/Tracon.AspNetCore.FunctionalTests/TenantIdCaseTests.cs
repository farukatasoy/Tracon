using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Letter case never distinguishes two tenants (phase 179).
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The failure these tests close: the tenant identifier was compared with
/// <c>Ordinal</c> in the authorization layer and with a bare <c>=</c> in the
/// stores. On SQL Server's case-insensitive default collation the storage
/// layer merged <c>acme</c> and <c>Acme</c> while the authorization layer kept
/// them apart; on PostgreSQL and SQLite the two layers disagreed the other
/// way. One deployment therefore answered "same tenant" and "different
/// tenant" for the same pair, depending on which layer was asked.
/// </para>
/// <para>
/// These run at the HTTP boundary on purpose. The fold happens in
/// <c>HttpTenantContext</c> and is read again by
/// <c>TraconEndpointFilter</c>; a unit test on either one alone cannot show
/// that the two surfaces agree (K-382).
/// </para>
/// </remarks>
public sealed class TenantIdCaseTests
{
    private const string TenantHeader = "X-Tracon-Tenant";

    [Fact]
    public async Task A_shifted_header_resolves_to_the_canonical_tenant()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        (await ReadTenantAsync(host, "Acme")).ShouldBe("acme");
        (await ReadTenantAsync(host, "ACME")).ShouldBe("acme");
        (await ReadTenantAsync(host, "acme")).ShouldBe("acme");
    }

    [Fact]
    public async Task A_shifted_header_reaches_the_data_written_by_the_canonical_tenant()
    {
        // The whole point of the fold: one tenant, one row set, whatever the
        // caller typed. Written as "acme", read back as "ACME".
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        using var created = await host.Client.SendAsync(
            Request(HttpMethod.Put, "/tracon/api/mcp-servers/shared", "acme", new
            {
                endpoint = "https://mcp.example.com/mcp",
                enabled = true,
                requiresApproval = true,
            }));

        created.EnsureSuccessStatusCode();

        using var read = await host.Client.SendAsync(
            Request(HttpMethod.Get, "/tracon/api/mcp-servers", "ACME", body: null));

        read.EnsureSuccessStatusCode();
        (await read.Content.ReadAsStringAsync()).ShouldContain("shared");
    }

    [Fact]
    public async Task A_shifted_header_is_admitted_by_a_canonical_allowlist_entry()
    {
        // Before the fold this answered 403: the allowlist held "acme" and the
        // request said "ACME", so the operator's own tenant was refused.
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
                options.AllowedTenants.Add("acme");
            }));

        (await ReadTenantAsync(host, "ACME")).ShouldBe("acme");
    }

    [Fact]
    public async Task A_canonical_header_is_admitted_by_a_shifted_allowlist_entry()
    {
        // The operator writes the allowlist by hand, so the capital letter is
        // as likely to be in configuration as in the request. BOTH sides fold.
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
                options.AllowedTenants.Add("Acme");
            }));

        (await ReadTenantAsync(host, "acme")).ShouldBe("acme");
    }

    [Fact]
    public async Task The_allowlist_still_rejects_a_tenant_that_is_not_on_it()
    {
        // 🚨 The fold must not widen the allowlist. A test that only proves
        // "ACME is now admitted" would also pass if the check were removed.
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
                options.AllowedTenants.Add("acme");
            }));

        using var rejected = await host.Client.SendAsync(
            Request(HttpMethod.Get, "/tracon/api/tenants/current", "other", body: null));

        rejected.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var alsoRejected = await host.Client.SendAsync(
            Request(HttpMethod.Get, "/tracon/api/tenants/current", "OTHER", body: null));

        alsoRejected.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_admin_endpoint_writes_the_canonical_tenant_taken_from_the_route()
    {
        // 🚨 The measured trigger of the original finding: the egress policy is
        // saved under the tenant spelled in the URL, but the runtime resolves
        // the canonical form. A policy saved as "Acme" was a row the runtime
        // never found, and ModelProviderRegistry applies NO restriction when
        // the row is missing - so the allow-list was fail-OPEN.
        await using var host = await TraconTestHost.StartAsync();

        using var saved = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/tenants/Acme/egress", UriKind.Relative),
            new { allowedProviders = new[] { "openai" } });

        saved.EnsureSuccessStatusCode();

        using var read = await host.Client.GetAsync(
            new Uri("/tracon/api/tenants/acme/egress", UriKind.Relative));

        read.EnsureSuccessStatusCode();
        (await read.Content.ReadAsStringAsync()).ShouldContain("openai");
    }

    [Fact]
    public async Task A_tenant_record_is_one_record_whatever_case_it_was_created_with()
    {
        // 🚨 The tenant registry's column is called 'slug', not 'tenant_id',
        // so neither the parameter choke-point gate nor the migration guard
        // can see it — and the endpoint's own description says the slug "is
        // the same text stored as 'tenant_id' on every other row". Without
        // the fold, PUT /api/tenants/Acme created a record that DELETE
        // /api/tenants/acme answered 404 for, leaving a row that matches no
        // running tenant.
        await using var host = await TraconTestHost.StartAsync();

        using var created = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/tenants/Acme", UriKind.Relative),
            new { displayName = "Acme Inc" });

        created.EnsureSuccessStatusCode();
        (await created.Content.ReadAsStringAsync()).ShouldContain("\"slug\":\"acme\"");

        using var deleted = await host.Client.DeleteAsync(
            new Uri("/tracon/api/tenants/acme", UriKind.Relative));

        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task<string> ReadTenantAsync(TraconTestHost host, string header)
    {
        using var response = await host.Client.SendAsync(
            Request(HttpMethod.Get, "/tracon/api/tenants/current", header, body: null));

        response.EnsureSuccessStatusCode();

        var current = await response.Content.ReadFromJsonAsync<CurrentTenantResponse>();

        return current.ShouldNotBeNull().TenantId;
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string tenant, object? body)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));

        request.Headers.Add(TenantHeader, tenant);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }
}
