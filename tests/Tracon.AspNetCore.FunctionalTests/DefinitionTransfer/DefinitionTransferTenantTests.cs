using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests.DefinitionTransfer;

/// <summary>The file carries no tenant, and the import writes to the tenant of the caller.</summary>
public sealed class DefinitionTransferTenantTests
{
    private const string TenantHeader = "X-Tracon-Tenant";

    [Fact]
    public async Task A_file_that_names_a_tenant_is_refused_at_the_top_and_inside_a_definition()
    {
        await using var host = await StartAsync();

        var top = TransferHost.NewBundle();

        top["tenantId"] = "acme";

        await RefusedAsync(host, top, "tenantId");

        var nested = TransferHost.NewBundle().WithAgent(TestData.Request("a"));

        nested["agents"]![0]!["definition"]!["tenantId"] = "acme";

        await RefusedAsync(host, nested, "tenantId");
    }

    [Fact]
    public async Task An_export_holds_only_the_callers_tenant_and_an_import_writes_only_to_it()
    {
        await using var host = await StartAsync();

        As(host, "tenant-a");
        await host.CreateAgentAsync(TestData.Request("only-in-a"));

        As(host, "tenant-b");
        await host.CreateAgentAsync(TestData.Request("only-in-b"));

        var bundleB = await host.ExportAsync();

        bundleB["agents"]!.AsArray().Select(agent => agent!["definition"]!["name"]!.GetValue<string>()).ToArray()
            .ShouldBe(["only-in-b"]);

        // The file written by tenant B lands in tenant C, and in no other tenant.
        As(host, "tenant-c");
        await host.ImportOkAsync(bundleB, dryRun: false);

        (await host.AgentExistsAsync("only-in-b")).ShouldBeTrue();

        As(host, "tenant-a");
        (await host.AgentExistsAsync("only-in-b")).ShouldBeFalse();
    }

    [Fact]
    public async Task A_credential_key_of_one_tenant_is_refused_in_another()
    {
        await using var host = await StartAsync();

        // The key space of a tenant is "{prefix}{tenant}:..."; the file moves the NAME of a key, and the target
        // tenant may only name keys of its own.
        As(host, "tenant-a");
        await host.CreateMcpServerAsync(
            "search",
            new { headerConfigurationKeys = new Dictionary<string, string>(StringComparer.Ordinal) { ["X-Api-Key"] = "Tracon:McpSecrets:tenant-a:SearchKey" } });

        var bundle = await host.ExportAsync("?types=mcpServers");

        As(host, "tenant-b");

        var dry = await host.ImportOkAsync(bundle, dryRun: true);

        dry["valid"]!.GetValue<bool>().ShouldBeFalse();
        TransferHost.Actions(dry, "mcpServer").ShouldBe(["search:rejected"]);

        // Back in the tenant it came from, the same file is what the tenant already holds.
        As(host, "tenant-a");

        var home = await host.ImportOkAsync(bundle, dryRun: true);

        TransferHost.Actions(home, "mcpServer").ShouldBe(["search:unchanged"]);
    }

    private static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(static builder => builder.UseTenancy(static options =>
        {
            options.Enabled = true;
            options.AllowHeaderResolution = true;
        }));

    private static void As(TraconTestHost host, string tenant)
    {
        host.Client.DefaultRequestHeaders.Remove(TenantHeader);
        host.Client.DefaultRequestHeaders.Add(TenantHeader, tenant);
    }

    private static async Task RefusedAsync(TraconTestHost host, JsonNode bundle, string mentions)
    {
        using var response = await host.Client.PostAsync(
            TransferHost.Api("definitions/import"),
            new StringContent(bundle.ToJsonString(), Encoding.UTF8, "application/json"));
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, text);
        text.ShouldContain(mentions);
    }
}
