using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Client.Generated;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The generated client sees the same tenant boundary a raw HTTP caller
/// does: it carries no server-side shortcut around
/// <c>TraconEndpointFilter</c>'s tenant check.
/// </summary>
public sealed class ClientTenantScopeTests
{
    private const string TenantHeader = "X-Tracon-Tenant";

    [Fact]
    public async Task Another_tenants_run_is_read_as_a_404_through_the_client()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        var runs = host.Services.GetRequiredService<IRunStore>();
        var runId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = "tenant-a",
        });

        // TraconTestHost's Client.BaseAddress carries no MapTracon
        // prefix; the generated client's relative paths need it added.
        host.Client.BaseAddress = new Uri("http://localhost/tracon/");
        host.Client.DefaultRequestHeaders.Remove(TenantHeader);
        host.Client.DefaultRequestHeaders.Add(TenantHeader, "tenant-b");

        var client = new TraconApiClient(host.Client);

        var exception = await Should.ThrowAsync<TraconApiException>(
            () => client.TraconGetRunAsync(runId, CancellationToken.None));

        exception.StatusCode.ShouldBe(404);
    }

    [Fact]
    public async Task The_owning_tenants_run_is_read_successfully_through_the_client()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        var runs = host.Services.GetRequiredService<IRunStore>();
        var runId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = "tenant-a",
        });

        host.Client.BaseAddress = new Uri("http://localhost/tracon/");
        host.Client.DefaultRequestHeaders.Remove(TenantHeader);
        host.Client.DefaultRequestHeaders.Add(TenantHeader, "tenant-a");

        var client = new TraconApiClient(host.Client);

        var run = await client.TraconGetRunAsync(runId, CancellationToken.None);

        run.Id.ShouldBe(runId);
    }
}
