using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Generated = Tracon.Client.Generated;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A bundle that carries an agent, a skill and an MCP server with only the members the document marks required is
/// imported through the generated client, and what it writes is what a request to the matching endpoint would write.
/// </summary>
/// <remarks>
/// The sweep of the whole document sends an empty bundle to the import operation, so it never builds one of the
/// request types a bundle embeds. The import reaches the agent definition, the skill and the server through the same
/// types as the single-record endpoints, so it has the same failure when a free-form member is sent as <c>null</c>.
/// </remarks>
public sealed class ImportDefinitionsMinimalAgentTests
{
    [Fact]
    public async Task A_bundle_with_a_minimal_agent_a_minimal_skill_and_a_minimal_mcp_server_is_imported()
    {
        await using var host = await OpenApiSnapshotTests.StartFullSurfaceHostAsync();
        host.Client.BaseAddress = new Uri("http://localhost/tracon/");
        var client = new Generated.TraconApiClient(host.Client);

        var bundle = new Generated.DefinitionBundle { Format = "tracon.definitions", FormatVersion = 1 };

        bundle.Agents.Add(new Generated.ExportedAgent
        {
            SourceVersion = 1,
            Definition = new Generated.AgentDefinitionRequest
            {
                Name = "imported-agent",
                Model = new Generated.ModelBinding { Provider = "echo", Model = "echo-1" },
            },
        });
        bundle.Skills.Add(new Generated.ExportedSkill
        {
            SourceVersion = 1,
            Definition = new Generated.AgentSkillRequest { Name = "imported-skill", Description = "A skill.", Instructions = "Do the thing." },
        });
        bundle.McpServers.Add(new Generated.ExportedMcpServer
        {
            Name = "imported-server",
            Definition = new Generated.McpServerRequest { Endpoint = "https://mcp.example.com/" },
        });

        var report = await client.TraconImportDefinitionsAsync(bundle, dryRun: false);

        report.Applied.ShouldBeTrue(string.Join("; ", report.Items.Select(static item => $"{item.Type} {item.Name}: {item.Action}")));
        report.Items.ShouldAllBe(static item => string.Equals(item.Action, "create", StringComparison.Ordinal));

        var agent = await client.TraconGetAgentAsync("imported-agent");

        agent.Definition.ShouldNotBeNull().Name.ShouldBe("imported-agent");

        var server = (await client.TraconListMcpServersAsync()).Single(static candidate => string.Equals(candidate.Name, "imported-server", StringComparison.Ordinal));

        server.RequiresApproval.ShouldBeTrue("an imported server that did not say otherwise asks for approval");
        server.Enabled.ShouldBeTrue();
    }
}
