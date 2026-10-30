using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests.DefinitionTransfer;

/// <summary>
/// The import is a second writer, so it may not accept what the save endpoints refuse: every refusal a
/// save makes, for the four kinds, is made again by the import with the same words.
/// </summary>
public sealed class DefinitionImportParityTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task An_agent_with_an_unknown_skill_is_refused_as_the_save_refuses_it()
    {
        await using var host = await TraconTestHost.StartAsync();
        var request = TestData.Request("a") with { SkillNames = ["nope"] };

        var detail = await SaveRefusalAsync(host, HttpMethod.Post, "agents", request, expected: HttpStatusCode.BadRequest);

        await AssertRefusedTheSameWayAsync(host, TransferHost.NewBundle().WithAgent(request), "agent", "a", detail);
    }

    [Fact]
    public async Task An_agent_with_an_unknown_tool_is_refused_as_the_save_refuses_it()
    {
        await using var host = await TraconTestHost.StartAsync();
        var request = TestData.Request("a") with { ToolNames = ["nope"] };

        var detail = await SaveRefusalAsync(host, HttpMethod.Post, "agents", request, expected: HttpStatusCode.BadRequest);

        await AssertRefusedTheSameWayAsync(host, TransferHost.NewBundle().WithAgent(request), "agent", "a", detail);
    }

    [Fact]
    public async Task An_agent_that_calls_an_agent_nobody_holds_is_refused_as_the_save_refuses_it()
    {
        await using var host = await TraconTestHost.StartAsync();
        var request = TestData.Request("a") with { CallableAgents = [CallableAgentReference.FromName("ghost")] };

        var detail = await SaveRefusalAsync(host, HttpMethod.Post, "agents", request, expected: HttpStatusCode.BadRequest);

        await AssertRefusedTheSameWayAsync(host, TransferHost.NewBundle().WithAgent(request), "agent", "a", detail);
    }

    [Fact]
    public async Task An_agent_that_calls_itself_is_refused_as_the_save_refuses_it()
    {
        await using var host = await TraconTestHost.StartAsync();
        var request = TestData.Request("a") with { CallableAgents = [CallableAgentReference.FromName("a")] };

        var detail = await SaveRefusalAsync(host, HttpMethod.Post, "agents", request, expected: HttpStatusCode.BadRequest);

        await AssertRefusedTheSameWayAsync(host, TransferHost.NewBundle().WithAgent(request), "agent", "a", detail);
    }

    [Fact]
    public async Task An_agent_without_a_model_is_refused_as_the_save_refuses_it()
    {
        await using var host = await TraconTestHost.StartAsync();
        var request = TestData.Request("a") with { Model = new ModelBinding { Provider = "echo", Model = string.Empty } };

        var detail = await SaveRefusalAsync(host, HttpMethod.Post, "agents", request, expected: HttpStatusCode.BadRequest);

        await AssertRefusedTheSameWayAsync(host, TransferHost.NewBundle().WithAgent(request), "agent", "a", detail);
    }

    [Fact]
    public async Task An_agent_named_like_a_code_agent_is_refused_as_the_save_refuses_it()
    {
        await using var host = await TraconTestHost.StartAsync(builder => builder.AddAgent(TestData.Definition()));
        var request = TestData.Request("kod-agent");

        var detail = await SaveRefusalAsync(host, HttpMethod.Put, "agents/kod-agent", request, expected: HttpStatusCode.Conflict);

        await AssertRefusedTheSameWayAsync(host, TransferHost.NewBundle().WithAgent(request), "agent", "kod-agent", detail);
    }

    [Fact]
    public async Task A_workflow_that_names_a_missing_function_is_refused_as_the_save_refuses_it()
    {
        await using var host = await TraconTestHost.StartAsync();
        var definition = new JsonObject
        {
            ["kind"] = "Sequential",
            ["nodes"] = new JsonArray(new JsonObject { ["name"] = "missing-fn", ["kind"] = "Function" }),
        };

        var detail = await SaveRawRefusalAsync(host, HttpMethod.Put, "workflows/flow", definition, HttpStatusCode.BadRequest);

        await AssertRefusedTheSameWayAsync(host, TransferHost.NewBundle().WithWorkflow("flow", definition), "workflow", "flow", detail);
    }

    [Fact]
    public async Task A_workflow_with_a_reserved_name_is_refused_as_the_save_refuses_it()
    {
        await using var host = await TraconTestHost.StartAsync();
        var definition = TransferHost.SequentialWorkflow("anyone");

        var detail = await SaveRawRefusalAsync(host, HttpMethod.Put, "workflows/functions", definition, HttpStatusCode.BadRequest);

        await AssertRefusedTheSameWayAsync(host, TransferHost.NewBundle().WithWorkflow("functions", definition), "workflow", "functions", detail);
    }

    [Fact]
    public async Task A_skill_with_an_invalid_name_is_refused_as_the_save_refuses_it()
    {
        await using var host = await TraconTestHost.StartAsync();
        var skill = new AgentSkillRequest { Name = "Invalid_Name", Description = "A skill.", Instructions = "Read." };

        var detail = await SaveRefusalAsync(host, HttpMethod.Put, "skills/Invalid_Name", skill, expected: HttpStatusCode.BadRequest);

        await AssertRefusedTheSameWayAsync(host, TransferHost.NewBundle().WithSkill("Invalid_Name"), "skill", "Invalid_Name", detail);
    }

    [Fact]
    public async Task A_skill_named_like_a_code_skill_is_refused_as_the_save_refuses_it()
    {
        await using var host = await TraconTestHost.StartAsync(builder => builder.AddSkill(new AgentSkillDefinition
        {
            TenantId = "default",
            Name = "in-code",
            Description = "Defined in code.",
            Instructions = "Read.",
        }));

        var skill = new AgentSkillRequest { Name = "in-code", Description = "A skill.", Instructions = "Read." };

        var detail = await SaveRefusalAsync(host, HttpMethod.Put, "skills/in-code", skill, expected: HttpStatusCode.Conflict);

        await AssertRefusedTheSameWayAsync(host, TransferHost.NewBundle().WithSkill("in-code"), "skill", "in-code", detail);
    }

    [Fact]
    public async Task A_skill_script_nobody_can_run_is_refused_as_the_save_refuses_it()
    {
        await using var host = await TraconTestHost.StartAsync();
        var skill = new AgentSkillRequest
        {
            Name = "scripted",
            Description = "A skill.",
            Instructions = "Read.",
            Scripts = [new AgentSkillScriptDefinition { Name = "go.zzz", Extension = "zzz", Content = "echo" }],
        };

        var detail = await SaveRefusalAsync(host, HttpMethod.Put, "skills/scripted", skill, expected: HttpStatusCode.BadRequest);

        var bundle = TransferHost.NewBundle();

        bundle["skills"]!.AsArray().Add(new JsonObject
        {
            ["sourceVersion"] = 1,
            ["definition"] = JsonSerializer.SerializeToNode(skill, Web),
        });

        await AssertRefusedTheSameWayAsync(host, bundle, "skill", "scripted", detail);
    }

    [Fact]
    public async Task An_MCP_server_that_is_not_http_is_refused_as_the_save_refuses_it()
    {
        await using var host = await TraconTestHost.StartAsync();
        var definition = TransferHost.McpDefinition("file:///usr/bin/server");

        var detail = await SaveRawRefusalAsync(host, HttpMethod.Put, "mcp-servers/local", definition, HttpStatusCode.BadRequest);

        await AssertRefusedTheSameWayAsync(host, TransferHost.NewBundle().WithMcpServer("local", definition), "mcpServer", "local", detail);
    }

    [Fact]
    public async Task A_credential_in_a_plain_header_is_refused_as_the_save_refuses_it()
    {
        await using var host = await TraconTestHost.StartAsync();
        var definition = TransferHost.McpDefinition();

        definition["headers"] = new JsonObject { ["Authorization"] = "Bearer plain-value" };

        var detail = await SaveRawRefusalAsync(host, HttpMethod.Put, "mcp-servers/leaky", definition, HttpStatusCode.BadRequest);

        await AssertRefusedTheSameWayAsync(host, TransferHost.NewBundle().WithMcpServer("leaky", definition), "mcpServer", "leaky", detail);
    }

    [Fact]
    public async Task A_masked_header_value_is_refused_as_the_save_refuses_it()
    {
        await using var host = await TraconTestHost.StartAsync();
        var definition = TransferHost.McpDefinition();

        definition["headers"] = new JsonObject { ["X-Region"] = "***" };

        var detail = await SaveRawRefusalAsync(host, HttpMethod.Put, "mcp-servers/masked", definition, HttpStatusCode.BadRequest);

        await AssertRefusedTheSameWayAsync(host, TransferHost.NewBundle().WithMcpServer("masked", definition), "mcpServer", "masked", detail);
    }

    [Fact]
    public async Task An_MCP_server_at_a_private_address_literal_is_refused_as_the_save_refuses_it()
    {
        await using var host = await TraconTestHost.StartAsync();
        var definition = TransferHost.McpDefinition("http://169.254.169.254/latest/meta-data");

        var detail = await SaveRawRefusalAsync(host, HttpMethod.Put, "mcp-servers/metadata", definition, HttpStatusCode.BadRequest);

        await AssertRefusedTheSameWayAsync(host, TransferHost.NewBundle().WithMcpServer("metadata", definition), "mcpServer", "metadata", detail);
    }

    [Fact]
    public async Task An_OAuth_server_without_a_client_id_is_refused_as_the_save_refuses_it()
    {
        await using var host = await TraconTestHost.StartAsync();
        var definition = TransferHost.McpDefinition();

        definition["oauthEnabled"] = true;

        var detail = await SaveRawRefusalAsync(host, HttpMethod.Put, "mcp-servers/oauth", definition, HttpStatusCode.BadRequest);

        await AssertRefusedTheSameWayAsync(host, TransferHost.NewBundle().WithMcpServer("oauth", definition), "mcpServer", "oauth", detail);
    }

    [Fact]
    public async Task An_Authorization_key_beside_OAuth_is_refused_as_the_save_refuses_it()
    {
        await using var host = await TraconTestHost.StartAsync();
        var definition = TransferHost.McpDefinition();

        definition["oauthEnabled"] = true;
        definition["oauthClientId"] = "client-1";
        definition["headerConfigurationKeys"] = new JsonObject { ["Authorization"] = "Tracon:McpSecrets:Token" };

        var detail = await SaveRawRefusalAsync(host, HttpMethod.Put, "mcp-servers/both", definition, HttpStatusCode.BadRequest);

        await AssertRefusedTheSameWayAsync(host, TransferHost.NewBundle().WithMcpServer("both", definition), "mcpServer", "both", detail);
    }

    [Fact]
    public async Task A_credential_key_outside_the_allowed_prefix_is_refused_as_the_save_refuses_it()
    {
        await using var host = await TraconTestHost.StartAsync();
        var definition = TransferHost.McpDefinition();

        definition["headerConfigurationKeys"] = new JsonObject { ["X-Api-Key"] = "Unrelated:Section:Key" };

        var detail = await SaveRawRefusalAsync(host, HttpMethod.Put, "mcp-servers/stray", definition, HttpStatusCode.BadRequest);

        await AssertRefusedTheSameWayAsync(host, TransferHost.NewBundle().WithMcpServer("stray", definition), "mcpServer", "stray", detail);
    }

    [Fact]
    public async Task A_skill_with_a_repeated_resource_name_is_refused_as_the_save_refuses_it()
    {
        await using var host = await TraconTestHost.StartAsync();
        var skill = new AgentSkillRequest
        {
            Name = "twice",
            Description = "A skill.",
            Instructions = "Read.",
            Resources =
            [
                new AgentSkillResourceDefinition { Name = "policy.md", Content = "One." },
                new AgentSkillResourceDefinition { Name = "policy.md", Content = "Two." },
            ],
        };

        var detail = await SaveRefusalAsync(host, HttpMethod.Put, "skills/twice", skill, expected: HttpStatusCode.BadRequest);
        var bundle = TransferHost.NewBundle();

        bundle["skills"]!.AsArray().Add(new JsonObject
        {
            ["sourceVersion"] = 1,
            ["definition"] = JsonSerializer.SerializeToNode(skill, Web),
        });

        await AssertRefusedTheSameWayAsync(host, bundle, "skill", "twice", detail);
    }

    private static async Task<string> SaveRefusalAsync<T>(TraconTestHost host, HttpMethod method, string path, T body, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(method, TransferHost.Api(path)) { Content = JsonContent.Create(body) };

        return await ReadRefusalAsync(host, request, expected);
    }

    private static async Task<string> SaveRawRefusalAsync(TraconTestHost host, HttpMethod method, string path, JsonObject body, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(method, TransferHost.Api(path))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };

        return await ReadRefusalAsync(host, request, expected);
    }

    private static async Task<string> ReadRefusalAsync(TraconTestHost host, HttpRequestMessage request, HttpStatusCode expected)
    {
        using var response = await host.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(expected, text);

        return JsonDocument.Parse(text).RootElement.GetProperty("detail").GetString()!;
    }

    private static async Task AssertRefusedTheSameWayAsync(TraconTestHost host, JsonNode bundle, string type, string name, string saveDetail)
    {
        var dry = await host.ImportOkAsync(bundle, dryRun: true);

        dry["valid"]!.GetValue<bool>().ShouldBeFalse(dry.ToJsonString());

        var item = TransferHost.Item(dry, type, name);

        item["action"]!.GetValue<string>().ShouldBe("rejected");

        var detail = string.Join(
            " ",
            item["messages"]!.AsArray()
                .Where(static message => string.Equals(message!["severity"]!.GetValue<string>(), "Error", StringComparison.Ordinal))
                .Select(static message => message!["message"]!.GetValue<string>()));

        detail.ShouldBe(saveDetail);

        // An apply is refused whole, with the report in the body.
        var (response, body) = await host.ImportAsync(bundle, dryRun: false);

        using (response)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            body!["applied"]!.GetValue<bool>().ShouldBeFalse();
        }
    }
}
