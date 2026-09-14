using System.Net;
using System.Text;
using System.Text.Json;
using Tracon.Capacity;

namespace Tracon.Capacity.Acceptance;

/// <summary>Tenant isolation under real, overlapping requests.</summary>
/// <remarks>
/// 🚨 Overlap is the point. Isolation in a quiet suite is a different claim
/// from isolation while both tenants are in flight in the same process, and it
/// is the second one a capacity report depends on.
/// </remarks>
public sealed class CapacityTenantTests : IDisposable
{
    private readonly HttpClient _client = new();

    private async Task<Guid> RunAsync(string tenant, string correlation)
    {
        var body = JsonSerializer.Serialize(new { message = CapacityPayload.RequestMessage(correlation, 256) });

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(AcceptanceEnvironment.BaseAddress + "/api/agents/" + CapacityContract.AgentName + "/run"))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        request.Headers.TryAddWithoutValidation(CapacityContract.TenantHeader, tenant);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", correlation);

        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("runId").GetGuid();
    }

    private async Task<HttpResponseMessage> GetAsync(string path, string tenant)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(AcceptanceEnvironment.BaseAddress + path));
        request.Headers.TryAddWithoutValidation(CapacityContract.TenantHeader, tenant);

        var response = await _client.SendAsync(request);
        request.Dispose();
        return response;
    }

    [Fact]
    public async Task Two_tenants_running_at_the_same_time_keep_their_own_answers()
    {
        var first = "tenant-a-" + Guid.NewGuid().ToString("N");
        var second = "tenant-b-" + Guid.NewGuid().ToString("N");

        var both = await Task.WhenAll(
            RunAsync(CapacityContract.TenantA, first),
            RunAsync(CapacityContract.TenantB, second));

        both[0].ShouldNotBe(both[1]);

        (await CapacityPathTests.CountAsync(
            $"SELECT COUNT(*) FROM {CapacityPathTests.Qualified("runs")} WHERE id = @id AND tenant_id = @tenant",
            ("id", both[0]), ("tenant", CapacityContract.TenantA))).ShouldBe(1);

        (await CapacityPathTests.CountAsync(
            $"SELECT COUNT(*) FROM {CapacityPathTests.Qualified("runs")} WHERE id = @id AND tenant_id = @tenant",
            ("id", both[1]), ("tenant", CapacityContract.TenantB))).ShouldBe(1);
    }

    [Fact]
    public async Task Reading_the_other_tenants_run_is_refused()
    {
        var runId = await RunAsync(CapacityContract.TenantA, "cross-read-" + Guid.NewGuid().ToString("N"));

        using var own = await GetAsync($"/api/runs/{runId}", CapacityContract.TenantA);
        own.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var foreign = await GetAsync($"/api/runs/{runId}", CapacityContract.TenantB);
        foreign.IsSuccessStatusCode.ShouldBeFalse();
    }

    [Fact]
    public async Task The_other_tenants_run_does_not_appear_in_a_listing()
    {
        var runId = await RunAsync(CapacityContract.TenantA, "cross-list-" + Guid.NewGuid().ToString("N"));

        using var listing = await GetAsync("/api/runs?limit=200", CapacityContract.TenantB);
        listing.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await listing.Content.ReadAsStringAsync()).ShouldNotContain(runId.ToString());
    }

    [Fact]
    public async Task The_other_tenants_event_stream_carries_no_content()
    {
        var runId = await RunAsync(CapacityContract.TenantA, "cross-stream-" + Guid.NewGuid().ToString("N"));

        using var stream = await GetAsync($"/api/runs/{runId}/events", CapacityContract.TenantB);

        if (stream.IsSuccessStatusCode)
        {
            // A 200 is only acceptable if it carries nothing of the other
            // tenant's: an empty stream is not a leak, a populated one is.
            (await stream.Content.ReadAsStringAsync()).ShouldNotContain("cross-stream-");
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }
}
