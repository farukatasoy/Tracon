using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests.DefinitionTransfer;

/// <summary>What a definition transfer test does to a host, in the calls a person would make.</summary>
internal static class TransferHost
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static Uri Api(string path) => new($"/tracon/api/{path}", UriKind.Relative);

    /// <summary>An empty file of the current format, for a test that writes the file by hand.</summary>
    public static JsonObject NewBundle()
        => new()
        {
            ["format"] = "tracon.definitions",
            ["formatVersion"] = 1,
            ["exportedAt"] = "2026-10-29T12:00:00Z",
            ["agents"] = new JsonArray(),
            ["workflows"] = new JsonArray(),
            ["skills"] = new JsonArray(),
            ["mcpServers"] = new JsonArray(),
        };

    public static JsonObject WithAgent(this JsonObject bundle, AgentDefinitionRequest request, int sourceVersion = 1)
    {
        bundle["agents"]!.AsArray().Add(new JsonObject
        {
            ["sourceVersion"] = sourceVersion,
            ["definition"] = JsonSerializer.SerializeToNode(request, Web),
        });

        return bundle;
    }

    public static JsonObject WithSkill(this JsonObject bundle, string name, string instructions = "Read the policy.", int sourceVersion = 1)
    {
        bundle["skills"]!.AsArray().Add(new JsonObject
        {
            ["sourceVersion"] = sourceVersion,
            ["definition"] = JsonSerializer.SerializeToNode(
                new AgentSkillRequest { Name = name, Description = "A skill.", Instructions = instructions },
                Web),
        });

        return bundle;
    }

    public static JsonObject WithWorkflow(this JsonObject bundle, string name, JsonObject definition, int sourceVersion = 1)
    {
        bundle["workflows"]!.AsArray().Add(new JsonObject
        {
            ["name"] = name,
            ["sourceVersion"] = sourceVersion,
            ["definition"] = definition,
        });

        return bundle;
    }

    public static JsonObject WithMcpServer(this JsonObject bundle, string name, JsonObject definition, params string[] headerNames)
    {
        bundle["mcpServers"]!.AsArray().Add(new JsonObject
        {
            ["name"] = name,
            ["definition"] = definition,
            ["headerNames"] = new JsonArray([.. headerNames.Select(static header => (JsonNode?)JsonValue.Create(header))]),
        });

        return bundle;
    }

    public static JsonObject McpDefinition(string endpoint = "https://mcp.example.test/mcp", bool requiresApproval = true)
        => new() { ["endpoint"] = endpoint, ["requiresApproval"] = requiresApproval };

    public static JsonObject SequentialWorkflow(params string[] agents)
        => new()
        {
            ["kind"] = "Sequential",
            ["agentNames"] = new JsonArray([.. agents.Select(static agent => (JsonNode?)JsonValue.Create(agent))]),
        };

    public static async Task<JsonElement> CreateSkillAsync(this TraconTestHost host, string name, string instructions = "Read the policy.")
    {
        using var response = await host.Client.PutAsJsonAsync(
            Api($"skills/{name}"),
            new AgentSkillRequest { Name = name, Description = "A skill.", Instructions = instructions });

        response.EnsureSuccessStatusCode();

        return await TraconTestHost.ReadJsonAsync(response);
    }

    public static async Task<JsonElement> CreateAgentAsync(this TraconTestHost host, AgentDefinitionRequest request)
    {
        using var response = await host.Client.PostAsJsonAsync(Api("agents"), request);

        response.EnsureSuccessStatusCode();

        return await TraconTestHost.ReadJsonAsync(response);
    }

    public static async Task<JsonElement> UpdateAgentAsync(this TraconTestHost host, AgentDefinitionRequest request)
    {
        using var response = await host.Client.PutAsJsonAsync(Api($"agents/{request.Name}"), request);

        response.EnsureSuccessStatusCode();

        return await TraconTestHost.ReadJsonAsync(response);
    }

    public static async Task CreateWorkflowAsync(this TraconTestHost host, string name, JsonObject definition)
    {
        using var response = await host.Client.PutAsync(
            Api($"workflows/{name}"),
            new StringContent(definition.ToJsonString(), Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();
    }

    public static async Task CreateMcpServerAsync(this TraconTestHost host, string name, object? extra = null)
    {
        var body = new JsonObject { ["endpoint"] = "https://mcp.example.test/mcp" };

        if (extra is not null)
        {
            foreach (var property in JsonSerializer.SerializeToNode(extra)!.AsObject())
            {
                body[property.Key] = property.Value?.DeepClone();
            }
        }

        using var response = await host.Client.PutAsync(
            Api($"mcp-servers/{name}"),
            new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();
    }

    public static async Task<JsonNode> ExportAsync(this TraconTestHost host, string query = "")
    {
        using var response = await host.Client.GetAsync(Api("definitions/export" + query));

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    public static async Task<(HttpResponseMessage Response, JsonNode? Body)> ImportAsync(
        this TraconTestHost host,
        JsonNode bundle,
        bool? dryRun = null,
        string? types = null)
    {
        var query = new List<string>();

        if (dryRun is { } value)
        {
            query.Add($"dryRun={(value ? "true" : "false")}");
        }

        if (types is not null)
        {
            query.Add($"types={types}");
        }

        var uri = Api("definitions/import" + (query.Count == 0 ? string.Empty : "?" + string.Join('&', query)));
        var response = await host.Client.PostAsync(uri, new StringContent(bundle.ToJsonString(), Encoding.UTF8, "application/json"));
        var text = await response.Content.ReadAsStringAsync();

        return (response, string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text));
    }

    public static async Task<JsonNode> ImportOkAsync(this TraconTestHost host, JsonNode bundle, bool dryRun, string? types = null)
    {
        var (response, body) = await host.ImportAsync(bundle, dryRun, types);

        using (response)
        {
            response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, body?.ToJsonString());
        }

        return body!;
    }

    /// <summary>The bundle without the one member that differs between two exports of the same definitions.</summary>
    public static string WithoutExportTime(JsonNode bundle)
    {
        var copy = bundle.DeepClone().AsObject();

        copy.Remove("exportedAt");

        return copy.ToJsonString();
    }

    public static string[] Actions(JsonNode report, string type)
        => [.. report["items"]!.AsArray()
            .Where(item => string.Equals(item!["type"]!.GetValue<string>(), type, StringComparison.Ordinal))
            .Select(item => $"{item!["name"]!.GetValue<string>()}:{item["action"]!.GetValue<string>()}")];

    public static JsonNode Item(JsonNode report, string type, string name)
        => report["items"]!.AsArray().Single(item =>
            string.Equals(item!["type"]!.GetValue<string>(), type, StringComparison.Ordinal) &&
            string.Equals(item["name"]!.GetValue<string>(), name, StringComparison.Ordinal))!;

    public static async Task<int> AgentVersionCountAsync(this TraconTestHost host, string name)
    {
        using var response = await host.Client.GetAsync(Api($"agents/{name}/versions"));

        return (await TraconTestHost.ReadJsonAsync(response)).GetArrayLength();
    }

    public static async Task<bool> AgentExistsAsync(this TraconTestHost host, string name)
    {
        using var response = await host.Client.GetAsync(Api($"agents/{name}"));

        return response.IsSuccessStatusCode;
    }
}
