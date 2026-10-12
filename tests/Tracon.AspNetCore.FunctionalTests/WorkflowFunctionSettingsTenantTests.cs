using System.Net;
using static Tracon.AspNetCore.FunctionalTests.WorkflowSettingsFixtures;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The same workflow name in two tenants carries two sets of settings that never mix (F-295).
/// </summary>
public sealed class WorkflowFunctionSettingsTenantTests
{
    [Fact]
    public async Task The_same_workflow_name_in_two_tenants_keeps_each_tenants_settings()
    {
        await using var host = await StartAsync(tenancy: true);

        await SaveOkAsync(host, "narrated", Chain("""{"voice":"alloy"}"""), tenant: "acme");
        await SaveOkAsync(host, "narrated", Chain("""{"voice":"nova"}"""), tenant: "globex");

        (await RunAsync(host, "narrated", tenant: "acme")).Output.ShouldBe("voice=alloy;lufs=-16;speed=Normal");
        (await RunAsync(host, "narrated", tenant: "globex")).Output.ShouldBe("voice=nova;lufs=-16;speed=Normal");

        foreach (var (tenant, expected) in new[] { ("acme", "alloy"), ("globex", "nova") })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/tracon/api/workflows/narrated");
            request.Headers.Add("X-Tracon-Tenant", tenant);

            using var response = await host.Client.SendAsync(request);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            (await Tracon.AspNetCore.FunctionalTests.Infrastructure.TraconTestHost.ReadJsonAsync(response))
                .GetProperty("nodes")[1].GetProperty("settings").GetProperty("voice").GetString().ShouldBe(expected);
        }
    }

    [Fact]
    public async Task A_save_in_one_tenant_does_not_change_the_settings_another_tenant_runs_with()
    {
        await using var host = await StartAsync(tenancy: true);

        await SaveOkAsync(host, "narrated", Chain("""{"voice":"alloy"}"""), tenant: "acme");
        await SaveOkAsync(host, "narrated", Chain("""{"voice":"nova"}"""), tenant: "globex");

        // acme saves again; globex must not notice.
        await SaveOkAsync(host, "narrated", Chain("""{"voice":"echo"}"""), tenant: "acme");

        (await RunAsync(host, "narrated", tenant: "globex")).Output.ShouldBe("voice=nova;lufs=-16;speed=Normal");
        (await RunAsync(host, "narrated", tenant: "acme")).Output.ShouldBe("voice=echo;lufs=-16;speed=Normal");
    }
}
