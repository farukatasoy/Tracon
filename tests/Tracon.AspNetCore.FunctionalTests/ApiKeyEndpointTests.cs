using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// CRUD behavior of the <c>/api/api-keys</c> endpoints (Phase 53).
/// </summary>
/// <remarks>
/// 🚨 The rule these tests guard: the raw key value is returned only in the
/// creation response; no listing call ever returns it again (section 53.2).
/// </remarks>
public sealed class ApiKeyEndpointTests
{
    [Fact]
    public async Task Creation_returns_the_raw_value_once()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/api/api-keys",
            new { name = "ci", scopes = new[] { "RunsRead" } });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var created = await response.Content.ReadFromJsonAsync<ApiKeyCreationResult>();

        created.ShouldNotBeNull();
        created.PlaintextKey.ShouldStartWith("ap_");
        created.Record.Name.ShouldBe("ci");
        created.Record.KeyPrefix.ShouldStartWith("ap_");
    }

    [Fact]
    public async Task Listing_does_not_return_the_raw_value_or_a_digest()
    {
        await using var host = await TraconTestHost.StartAsync();

        var created = await CreateKeyAsync(host, "ci", "RunsRead");

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/api-keys", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldNotContain(created.PlaintextKey);

        var keys = await response.Content.ReadFromJsonAsync<IReadOnlyList<ApiKeyRecord>>();
        keys.ShouldNotBeNull();
        keys.ShouldHaveSingleItem().Id.ShouldBe(created.Record.Id);
    }

    [Fact]
    public async Task Empty_name_is_rejected()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/api/api-keys",
            new { name = "", scopes = new[] { "RunsRead" } });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Empty_scope_list_is_rejected()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/api/api-keys",
            new { name = "ci", scopes = Array.Empty<string>() });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unknown_scope_is_rejected()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/api/api-keys",
            new { name = "ci", scopes = new[] { "runs:all" } });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Revoked_key_appears_inactive_in_the_list()
    {
        await using var host = await TraconTestHost.StartAsync();

        var created = await CreateKeyAsync(host, "ci", "RunsRead");

        using var revoke = await host.Client.DeleteAsync(
            new Uri($"/tracon/api/api-keys/{created.Record.Id}", UriKind.Relative));

        revoke.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var keys = await host.Client.GetFromJsonAsync<IReadOnlyList<ApiKeyRecord>>(
            new Uri("/tracon/api/api-keys", UriKind.Relative));

        keys.ShouldNotBeNull().ShouldHaveSingleItem().IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Revoking_a_nonexistent_key_returns_404()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.DeleteAsync(
            new Uri($"/tracon/api/api-keys/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    internal static async Task<ApiKeyCreationResult> CreateKeyAsync(
        TraconTestHost host,
        string name,
        params string[] scopes)
    {
        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/api/api-keys",
            new { name, scopes });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ApiKeyCreationResult>()).ShouldNotBeNull();
    }
}
