using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests.DefinitionTransfer;

/// <summary>What a file carries and what it leaves out.</summary>
public sealed class DefinitionExportTests
{
    [Fact]
    public async Task Every_definition_of_the_four_kinds_is_written_in_name_order_and_the_same_bytes_every_time()
    {
        await using var host = await TraconTestHost.StartAsync();

        await host.CreateSkillAsync("zeta-skill");
        await host.CreateSkillAsync("alpha-skill");
        await host.CreateAgentAsync(TestData.Request("zeta"));
        await host.CreateAgentAsync(TestData.Request("alpha"));
        await host.CreateMcpServerAsync("search");
        await host.CreateWorkflowAsync("flow", TransferHost.SequentialWorkflow("alpha"));

        var first = await host.ExportAsync();
        var second = await host.ExportAsync();

        TransferHost.WithoutExportTime(first).ShouldBe(TransferHost.WithoutExportTime(second));
        Names(first, "agents", agent => agent["definition"]!["name"]!.GetValue<string>()).ShouldBe(["alpha", "zeta"]);
        Names(first, "skills", skill => skill["definition"]!["name"]!.GetValue<string>()).ShouldBe(["alpha-skill", "zeta-skill"]);
        Names(first, "workflows", workflow => workflow["name"]!.GetValue<string>()).ShouldBe(["flow"]);
        Names(first, "mcpServers", server => server["name"]!.GetValue<string>()).ShouldBe(["search"]);
    }

    [Fact]
    public async Task A_file_carries_no_tenant_no_time_no_origin_and_no_history()
    {
        await using var host = await TraconTestHost.StartAsync();

        await host.CreateSkillAsync("policy");
        await host.CreateAgentAsync(TestData.Request("worker"));
        await host.CreateMcpServerAsync("search");
        await host.CreateWorkflowAsync("flow", TransferHost.SequentialWorkflow("worker"));

        var text = (await host.ExportAsync()).ToJsonString();

        foreach (var member in new[] { "tenantId", "origin", "updatedAt", "createdAt", "\"version\"", "\"id\"" })
        {
            text.ShouldNotContain(member);
        }
    }

    [Fact]
    public async Task Types_writes_every_definition_of_those_kinds_and_nothing_else()
    {
        await using var host = await TraconTestHost.StartAsync();

        await host.CreateSkillAsync("policy");
        await host.CreateAgentAsync(TestData.Request("worker") with { SkillNames = ["policy"] });

        var bundle = await host.ExportAsync("?types=skills");

        bundle["skills"]!.AsArray().Count.ShouldBe(1);
        bundle["agents"]!.AsArray().Count.ShouldBe(0);
    }

    [Fact]
    public async Task A_named_agent_brings_what_it_needs_and_not_what_it_does_not()
    {
        await using var host = await TraconTestHost.StartAsync();

        await host.CreateSkillAsync("policy");
        await host.CreateSkillAsync("unrelated-skill");
        await host.CreateAgentAsync(TestData.Request("house-style", "Be polite."));
        await host.CreateAgentAsync(TestData.Request("callee"));
        await host.CreateAgentAsync(TestData.Request("lead") with
        {
            SkillNames = ["policy"],
            SharedInstructionsName = "house-style",
            CallableAgents = [CallableAgentReference.FromName("callee")],
        });
        await host.CreateAgentAsync(TestData.Request("other"));

        var bundle = await host.ExportAsync("?agent=lead");

        Names(bundle, "agents", agent => agent["definition"]!["name"]!.GetValue<string>()).ShouldBe(["callee", "house-style", "lead"]);
        Names(bundle, "skills", skill => skill["definition"]!["name"]!.GetValue<string>()).ShouldBe(["policy"]);
    }

    [Fact]
    public async Task A_named_agent_brings_the_MCP_servers_whose_tools_and_resources_it_uses()
    {
        await using var host = await TraconTestHost.StartAsync();

        foreach (var server in new[] { "lookup-srv", "docs-srv", "unrelated", "a", "a_b" })
        {
            await host.CreateMcpServerAsync(server);
        }

        // Saved through the store: creating the agent over HTTP would need the tools to be live on a real server.
        await host.Services.GetRequiredService<IAgentDefinitionStore>().SaveAsync(new AgentDefinition
        {
            Name = "finder",
            Instructions = "Look things up.",
            Model = TestData.Model(),
            ToolNames = ["lookup-srv_lookup", "a_b_search", "some_code_tool"],
            McpResourceUris = ["docs-srv:file://guide.md"],
        });

        var bundle = await host.ExportAsync("?agent=finder");

        // A tool name belongs to every server whose name prefixes it ("a" and "a_b" both match "a_b_search"): the file may
        // carry one server too many and never one too few. A name that matches no server (a code tool) brings nothing.
        Names(bundle, "mcpServers", server => server["name"]!.GetValue<string>()).ShouldBe(["a", "a_b", "docs-srv", "lookup-srv"]);
    }

    [Fact]
    public async Task Naming_definitions_and_kinds_in_one_call_is_refused()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(TransferHost.Api("definitions/export?agent=a&types=skills"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_named_definition_that_is_not_stored_is_a_404()
    {
        await using var host = await TraconTestHost.StartAsync(builder => builder.AddAgent(TestData.Definition()));

        using (var absent = await host.Client.GetAsync(TransferHost.Api("definitions/export?agent=nobody")))
        {
            absent.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        // An agent defined in code is not stored, so it is not exportable by name either.
        using var code = await host.Client.GetAsync(TransferHost.Api("definitions/export?agent=kod-agent"));

        code.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_closure_that_reaches_a_code_agent_leaves_it_out_and_says_so()
    {
        await using var host = await TraconTestHost.StartAsync(builder => builder.AddAgent(TestData.Definition()));

        await host.CreateAgentAsync(TestData.Request("caller") with { CallableAgents = [CallableAgentReference.FromName("kod-agent")] });

        var bundle = await host.ExportAsync("?agent=caller");

        Names(bundle, "agents", agent => agent["definition"]!["name"]!.GetValue<string>()).ShouldBe(["caller"]);
        bundle["warnings"]!.AsArray().Select(warning => warning!.GetValue<string>()).ShouldContain(
            static warning => warning.Contains("kod-agent", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_pin_the_file_cannot_hold_is_warned_about_where_it_is_made()
    {
        await using var host = await TraconTestHost.StartAsync();

        await host.CreateAgentAsync(TestData.Request("x", "one"));
        await host.UpdateAgentAsync(TestData.Request("x", "two"));
        await host.CreateAgentAsync(TestData.Request("a") with { CallableAgents = [new CallableAgentReference { Name = "x", Version = 1 }] });

        // "x" is at version 2 in the file, and "a" pins version 1: an import would refuse that.
        var bundle = await host.ExportAsync("?agent=a");

        bundle["warnings"]!.AsArray().Select(warning => warning!.GetValue<string>()).ShouldContain(
            static warning => warning.Contains("pins callable agent 'x' to version 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_export_is_written_to_the_audit_trail_with_counts_and_never_content()
    {
        await using var host = await TraconTestHost.StartAsync();

        await host.CreateAgentAsync(TestData.Request("worker", "Private instruction wording."));
        await host.ExportAsync();

        using var response = await host.Client.GetAsync(TransferHost.Api("audit?limit=100"));
        var entry = (await TraconTestHost.ReadJsonAsync(response)).EnumerateArray()
            .Single(static entry => string.Equals(entry.GetProperty("action").GetString(), "definitions.export", StringComparison.Ordinal));

        entry.GetProperty("after").GetString()!.ShouldNotContain("Private");
        entry.GetProperty("after").GetString()!.ShouldContain("\"agents\":1");
    }

    private static string[] Names(JsonNode bundle, string member, Func<JsonNode, string> name)
        => [.. bundle[member]!.AsArray().Select(item => name(item!))];
}
