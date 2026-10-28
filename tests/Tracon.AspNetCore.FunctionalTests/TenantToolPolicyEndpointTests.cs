using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// <c>GET</c>, <c>PUT</c> and <c>DELETE /api/tenants/{tenantId}/tools</c>: what they accept, what they refuse, and what
/// they leave in the audit trail.
/// </summary>
/// <remarks>
/// The cross-tenant rule (a key bound to one tenant cannot write another tenant's policy) is in
/// <c>CrossTenantAuthorityTests</c>, with the rest of the endpoints that take the tenant from the route.
/// </remarks>
public sealed class TenantToolPolicyEndpointTests
{
    private static readonly Uri AcmeUri = new("/tracon/api/tenants/acme/tools", UriKind.Relative);

    // --- Reading ---

    [Fact]
    public async Task A_tenant_with_no_policy_answers_200_with_two_null_lists_and_the_switch()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(AcmeUri, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await TraconTestHost.ReadJsonAsync(response);

        body.GetProperty("tenantId").GetString().ShouldBe("acme");
        body.GetProperty("allowedTools").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("grantedPermissions").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("updatedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("requirePolicy").GetBoolean().ShouldBeFalse();
    }

    // --- Writing ---

    [Fact]
    public async Task A_saved_policy_is_read_back_and_the_tenant_is_folded_to_lower_case()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var saved = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/tenants/Acme/tools", UriKind.Relative),
            new { allowedTools = new[] { "get_weather" }, grantedPermissions = new[] { "orders.read" } },
            TestContext.Current.CancellationToken);

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var savedBody = await TraconTestHost.ReadJsonAsync(saved);

        savedBody.GetProperty("tenantId").GetString().ShouldBe("acme");

        using var read = await host.Client.GetAsync(AcmeUri, TestContext.Current.CancellationToken);
        var body = await TraconTestHost.ReadJsonAsync(read);

        body.GetProperty("allowedTools").EnumerateArray().Select(static item => item.GetString()).ShouldBe(["get_weather"]);
        body.GetProperty("grantedPermissions").EnumerateArray().Select(static item => item.GetString()).ShouldBe(["orders.read"]);
        body.GetProperty("updatedAt").ValueKind.ShouldBe(JsonValueKind.String);
    }

    [Fact]
    public async Task Each_entry_is_trimmed_and_a_repeated_entry_is_saved_once()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var saved = await host.Client.PutAsJsonAsync(
            AcmeUri,
            new
            {
                allowedTools = new[] { " get_weather ", "get_weather", "search_knowledge", "get_weather\t" },
                grantedPermissions = new[] { "orders.read", " orders.read" },
            },
            TestContext.Current.CancellationToken);

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var policy = await host.Services.GetRequiredService<ITenantToolPolicyStore>().GetAsync("acme", TestContext.Current.CancellationToken);

        policy.ShouldNotBeNull();
        policy.AllowedTools.ShouldBe(["get_weather", "search_knowledge"]);
        policy.GrantedPermissions.ShouldBe(["orders.read"]);
    }

    [Fact]
    public async Task A_missing_list_is_an_empty_list_and_not_an_absent_policy()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var saved = await host.Client.PutAsJsonAsync(AcmeUri, new { }, TestContext.Current.CancellationToken);

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var policy = await host.Services.GetRequiredService<ITenantToolPolicyStore>().GetAsync("acme", TestContext.Current.CancellationToken);

        // A policy that names nothing refuses everything; no policy at all would have refused nothing.
        policy.ShouldNotBeNull();
        policy.AllowedTools.ShouldBeEmpty();
        policy.GrantedPermissions.ShouldBeEmpty();
    }

    public static TheoryData<string, string> RefusedBodies => new()
    {
        { """{"allowedTools":["get_weather",""]}""", "empty entry" },
        { """{"allowedTools":["   "]}""", "empty entry" },
        { """{"grantedPermissions":[null]}""", "empty entry" },
        { """{"allowedTools":["get\u0001weather"]}""", "control character" },
        { """{"grantedPermissions":["orders\nread"]}""", "control character" },
        { """{"allowedTools":["get weather\r\nnext"]}""", "control character" },
    };

    [Theory]
    [MemberData(nameof(RefusedBodies))]
    public async Task A_body_with_an_empty_entry_or_a_control_character_is_refused_and_nothing_is_saved(string body, string reason)
    {
        await using var host = await TraconTestHost.StartAsync();

        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using var response = await host.Client.PutAsync(AcmeUri, content, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain(reason);
        (await host.Services.GetRequiredService<ITenantToolPolicyStore>().GetAsync("acme", TestContext.Current.CancellationToken))
            .ShouldBeNull();
    }

    [Fact]
    public async Task The_longest_entry_is_64_characters_and_a_longer_one_is_refused()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var accepted = await host.Client.PutAsJsonAsync(
            AcmeUri,
            new { allowedTools = new[] { new string('a', 64) } },
            TestContext.Current.CancellationToken);

        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var refused = await host.Client.PutAsJsonAsync(
            AcmeUri,
            new { grantedPermissions = new[] { new string('a', 65) } },
            TestContext.Current.CancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("at most 64 characters");
    }

    [Fact]
    public async Task A_list_holds_at_most_200_entries()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var accepted = await host.Client.PutAsJsonAsync(
            AcmeUri,
            new { allowedTools = Enumerable.Range(0, 200).Select(static index => $"tool_{index}").ToArray() },
            TestContext.Current.CancellationToken);

        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var refused = await host.Client.PutAsJsonAsync(
            AcmeUri,
            new { allowedTools = Enumerable.Range(0, 201).Select(static index => $"tool_{index}").ToArray() },
            TestContext.Current.CancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("at most 200 entries");

        // The refused save left the accepted one in place.
        (await host.Services.GetRequiredService<ITenantToolPolicyStore>().GetAsync("acme", TestContext.Current.CancellationToken))
            .ShouldNotBeNull().AllowedTools.Count.ShouldBe(200);
    }

    [Fact]
    public async Task A_tenant_key_that_is_not_valid_is_refused()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/tenants/bad%20tenant/tools", UriKind.Relative),
            new { allowedTools = new[] { "get_weather" } },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("Invalid tenant key");
    }

    // --- Deleting ---

    [Fact]
    public async Task Deleting_a_policy_removes_it_and_a_second_delete_is_404()
    {
        await using var host = await TraconTestHost.StartAsync();

        await host.Client.PutAsJsonAsync(AcmeUri, new { allowedTools = new[] { "get_weather" } }, TestContext.Current.CancellationToken);

        using var deleted = await host.Client.DeleteAsync(AcmeUri, TestContext.Current.CancellationToken);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var read = await host.Client.GetAsync(AcmeUri, TestContext.Current.CancellationToken);
        (await TraconTestHost.ReadJsonAsync(read)).GetProperty("allowedTools").ValueKind.ShouldBe(JsonValueKind.Null);

        using var again = await host.Client.DeleteAsync(AcmeUri, TestContext.Current.CancellationToken);
        again.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // --- The audit trail ---

    [Fact]
    public async Task Saving_a_policy_writes_an_audit_entry_that_carries_both_lists()
    {
        // The test host's ambient tenant is "default"; /api/audit/{entity} filters by it, so the policy that is
        // saved and read back is the default tenant's own.
        await using var host = await TraconTestHost.StartAsync();

        await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/tenants/default/tools", UriKind.Relative),
            new { allowedTools = new[] { "get_weather" }, grantedPermissions = new[] { "orders.read" } },
            TestContext.Current.CancellationToken);

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/api/audit/tenant_tool_policy:default", UriKind.Relative),
            TestContext.Current.CancellationToken);

        var entries = await response.Content.ReadFromJsonAsync<IReadOnlyList<AuditEntry>>(TestContext.Current.CancellationToken);

        var entry = entries.ShouldNotBeNull().ShouldHaveSingleItem();
        entry.Action.ShouldBe("tenant_tool_policy.save");
        entry.After.ShouldNotBeNull();

        using var payload = JsonDocument.Parse(entry.After);

        payload.RootElement.GetProperty("allowedTools").EnumerateArray().Select(static item => item.GetString()).ShouldBe(["get_weather"]);
        payload.RootElement.GetProperty("grantedPermissions").EnumerateArray().Select(static item => item.GetString()).ShouldBe(["orders.read"]);
    }

    [Fact]
    public async Task Deleting_a_policy_writes_an_audit_entry()
    {
        await using var host = await TraconTestHost.StartAsync();

        await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/tenants/default/tools", UriKind.Relative),
            new { allowedTools = new[] { "get_weather" } },
            TestContext.Current.CancellationToken);
        await host.Client.DeleteAsync(new Uri("/tracon/api/tenants/default/tools", UriKind.Relative), TestContext.Current.CancellationToken);

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/api/audit/tenant_tool_policy:default", UriKind.Relative),
            TestContext.Current.CancellationToken);

        var entries = await response.Content.ReadFromJsonAsync<IReadOnlyList<AuditEntry>>(TestContext.Current.CancellationToken);

        entries.ShouldNotBeNull().Select(static entry => entry.Action).ShouldContain("tenant_tool_policy.delete", StringComparer.Ordinal);
    }
}
