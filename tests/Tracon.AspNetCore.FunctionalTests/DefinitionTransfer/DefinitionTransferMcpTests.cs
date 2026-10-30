using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests.DefinitionTransfer;

/// <summary>
/// An MCP server travels with the names of its headers and never a value, and an agent that needs a tool of a server
/// the file creates waits for that server.
/// </summary>
public sealed class DefinitionTransferMcpTests
{
    private const string PlainValue = "eu-west-1-plain-value";

    [Fact]
    public async Task An_export_carries_header_names_and_key_names_and_never_a_value()
    {
        await using var host = await TraconTestHost.StartAsync();

        await host.CreateMcpServerAsync(
            "search",
            new
            {
                headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["X-Region"] = PlainValue },
                headerConfigurationKeys = new Dictionary<string, string>(StringComparer.Ordinal) { ["X-Api-Key"] = "Tracon:McpSecrets:SearchKey" },
            });

        var bundle = await host.ExportAsync("?types=mcpServers");
        var server = bundle["mcpServers"]![0]!;

        bundle.ToJsonString().ShouldNotContain(PlainValue);
        server["headerNames"]!.AsArray().Select(name => name!.GetValue<string>()).ToArray().ShouldBe(["X-Region"]);
        // "headers" is null, which an import reads as "keep the map the target holds": the file has no value to give.
        server["definition"]!["headers"].ShouldBeNull();
        server["definition"]!["headerConfigurationKeys"]!["X-Api-Key"]!.GetValue<string>().ShouldBe("Tracon:McpSecrets:SearchKey");
    }

    [Fact]
    public async Task An_import_over_an_existing_server_keeps_the_header_values_the_target_holds()
    {
        await using var target = await TraconTestHost.StartAsync();

        await target.CreateMcpServerAsync(
            "search",
            new { headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["X-Region"] = "ap-1-target-value" } });

        var bundle = TransferHost.NewBundle()
            .WithMcpServer("search", TransferHost.McpDefinition(), "X-Region");

        bundle["mcpServers"]![0]!["definition"]!["description"] = "Changed in the file.";

        var applied = await target.ImportOkAsync(bundle, dryRun: false);

        TransferHost.Actions(applied, "mcpServer").ShouldBe(["search:update"]);

        var stored = await target.Services.GetRequiredService<IMcpServerStore>().GetAsync("default", "search");

        stored.ShouldNotBeNull().Description.ShouldBe("Changed in the file.");
        stored.Headers["X-Region"].ShouldBe("ap-1-target-value");
    }

    [Fact]
    public async Task A_new_server_warns_that_its_header_values_have_to_be_entered_in_the_target()
    {
        await using var target = await TraconTestHost.StartAsync();

        var bundle = TransferHost.NewBundle().WithMcpServer("search", TransferHost.McpDefinition(), "X-Region");
        var dry = await target.ImportOkAsync(bundle, dryRun: true);

        dry["valid"]!.GetValue<bool>().ShouldBeTrue();

        var warning = TransferHost.Item(dry, "mcpServer", "search")["messages"]!.AsArray()
            .Single(message => string.Equals(message!["code"]!.GetValue<string>(), "import_mcp_header_values_needed", StringComparison.Ordinal))!;

        warning["severity"]!.GetValue<string>().ShouldBe("Warning");
        warning["message"]!.GetValue<string>().ShouldContain("X-Region");
    }

    [Fact]
    public async Task A_server_that_uses_OAuth_warns_that_it_has_to_be_authorized_again()
    {
        await using var target = await TraconTestHost.StartAsync();

        var definition = TransferHost.McpDefinition();

        definition["oauthEnabled"] = true;
        definition["oauthClientId"] = "client-1";

        var dry = await target.ImportOkAsync(TransferHost.NewBundle().WithMcpServer("search", definition), dryRun: true);

        TransferHost.Item(dry, "mcpServer", "search")["messages"]!.AsArray()
            .Select(message => message!["code"]!.GetValue<string>())
            .ShouldContain(static code => string.Equals(code, "import_mcp_oauth_reauthorize", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_agent_that_needs_a_tool_of_a_new_server_waits_for_the_server_and_then_goes_through()
    {
        await using var server = await LookupMcpServer.StartAsync("from-the-file");
        await using var target = await TraconTestHost.StartAsync(
            builder => builder.UseMcp(),
            configureServices: static services => services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true));

        var definition = TransferHost.McpDefinition(server.Endpoint.ToString(), requiresApproval: false);
        var bundle = TransferHost.NewBundle()
            .WithMcpServer("lookup-srv", definition)
            .WithAgent(TestData.Request("finder", "Look it up.") with { ToolNames = ["lookup-srv_lookup"] });

        // One step cannot do it: a server's tools come from the live server, which is not saved yet.
        var dry = await target.ImportOkAsync(bundle, dryRun: true);

        dry["valid"]!.GetValue<bool>().ShouldBeFalse();
        TransferHost.Actions(dry, "agent").ShouldBe(["finder:blocked"]);
        TransferHost.Actions(dry, "mcpServer").ShouldBe(["lookup-srv:create"]);
        TransferHost.Item(dry, "agent", "finder")["messages"]![0]!["code"]!.GetValue<string>().ShouldBe("import_mcp_stage_required");

        var (refused, _) = await target.ImportAsync(bundle, dryRun: false);

        using (refused)
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using (var servers = await target.Client.GetAsync(TransferHost.Api("mcp-servers")))
        {
            (await TraconTestHost.ReadJsonAsync(servers)).GetArrayLength().ShouldBe(0);
        }

        // Step one: the servers only.
        var first = await target.ImportOkAsync(bundle, dryRun: false, types: "mcpServers");

        first["applied"]!.GetValue<bool>().ShouldBeTrue();
        TransferHost.Actions(first, "agent").ShouldBe(["finder:skipped"]);
        (await target.AgentExistsAsync("finder")).ShouldBeFalse();

        // Step two: the same file; the server is what the target holds now, and the agent goes through.
        var second = await target.ImportOkAsync(bundle, dryRun: false);

        second["valid"]!.GetValue<bool>().ShouldBeTrue(second.ToJsonString());
        TransferHost.Actions(second, "mcpServer").ShouldBe(["lookup-srv:unchanged"]);
        TransferHost.Actions(second, "agent").ShouldBe(["finder:create"]);
        (await target.AgentExistsAsync("finder")).ShouldBeTrue();
    }
}
