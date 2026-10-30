using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>
/// The definition transfer on a real database server: a file leaves one database and enters another, and
/// what is read back is the same file.
/// </summary>
/// <remarks>
/// <para>
/// Two hosts, and unlike the two-node set they share NOTHING: each has its own schema, so a definition that arrives in
/// the second host crossed the file and nothing else. A provider's class derives from this one and says which server it
/// runs on; the scenario is the same on PostgreSQL and SQL Server because the claim is about content, and a store may
/// hand a definition back a little different from the one it was given (a number written another way, a member the
/// serializer leaves out). The in-memory stores of the functional tests cannot show that.
/// </para>
/// </remarks>
public abstract class DefinitionTransferAcceptanceContract
{
    private static Uri Api(string path) => new($"/tracon/api/{path}", UriKind.Relative);

    /// <summary>Gets a schema no other test uses, on the class's server.</summary>
    /// <returns>The database one host connects to.</returns>
    protected abstract TwoNodeDatabase NewDatabase();

    /// <summary>
    /// Export, import into an empty host on another database, export again: the two files are the same bytes but the
    /// export time, the imported agent answers a run, and importing the file again changes nothing.
    /// </summary>
    [Fact]
    public async Task A_file_survives_a_trip_through_two_databases_byte_for_byte_and_the_imported_agent_runs()
    {
        await using var source = await StartAsync(NewDatabase());
        await using var target = await StartAsync(NewDatabase());

        await PutAsync(source, "skills/policy", new { name = "policy", description = "A skill.", instructions = "Read the policy." });
        await PostAsync(source, "agents", Agent("worker", "Do the work."));
        await PostAsync(source, "agents", Agent("triage", "Route the request.", temperature: 0.7f) with
        {
            SkillNames = ["policy"],
            CallableAgents = [CallableAgentReference.FromName("worker")],
        });
        await PutAsync(source, "workflows/flow", new { kind = "Sequential", agentNames = new[] { "triage", "worker" } });
        await PutAsync(source, "mcp-servers/search", new { endpoint = "https://mcp.example.test/mcp", requiresApproval = true });

        var bundle = await ExportAsync(source);

        // The target is empty, so the file is the only way anything gets there.
        var imported = await ImportAsync(target, bundle, dryRun: false);

        imported["applied"]!.GetValue<bool>().ShouldBeTrue(imported.ToJsonString());

        var echoed = await ExportAsync(target);

        Strip(echoed).ShouldBe(Strip(bundle));

        // The agent really runs: what arrived is a definition that compiles and answers, not only one that reads back.
        using (var run = await target.Client.PostAsJsonAsync(
                   Api("agents/triage/run"),
                   new AgentRunRequest { Message = "hello from the target" }))
        {
            run.StatusCode.ShouldBe(HttpStatusCode.OK, await run.Content.ReadAsStringAsync());
        }

        // The same file again, against a real store: the number 0.7 and every member came back the way the file had
        // them, so nothing reads as changed and no version is added.
        var again = await ImportAsync(target, bundle, dryRun: false);

        again["written"]!.AsArray().Count.ShouldBe(0, again.ToJsonString());
        again["items"]!.AsArray().ShouldAllBe(static item => string.Equals(item!["action"]!.GetValue<string>(), "unchanged", StringComparison.Ordinal));

        using var versions = await target.Client.GetAsync(Api("agents/triage/versions"));

        (await TraconTestHost.ReadJsonAsync(versions)).GetArrayLength().ShouldBe(1);
    }

    /// <summary>A pin is written with the version the TARGET database gave the agent, not the source's number.</summary>
    [Fact]
    public async Task A_pin_lands_on_the_version_the_target_database_gave_the_agent()
    {
        await using var source = await StartAsync(NewDatabase());
        await using var target = await StartAsync(NewDatabase());

        await PostAsync(source, "agents", Agent("callee", "one"));
        await PutAsync(source, "agents/callee", Agent("callee", "two"));
        await PutAsync(source, "agents/callee", Agent("callee", "three"));
        await PostAsync(source, "agents", Agent("caller", "Uses the callee.") with
        {
            CallableAgents = [new CallableAgentReference { Name = "callee", Version = 3 }],
        });

        // The target's "callee" is another agent at version 1.
        await PostAsync(target, "agents", Agent("callee", "a different agent"));

        var report = await ImportAsync(target, await ExportAsync(source, "?agent=caller"), dryRun: false);

        report["applied"]!.GetValue<bool>().ShouldBeTrue(report.ToJsonString());

        using var detail = await target.Client.GetAsync(Api("agents/caller"));
        var pin = (await TraconTestHost.ReadJsonAsync(detail))
            .GetProperty("definition").GetProperty("callableAgents")[0].GetProperty("version").GetInt32();

        pin.ShouldBe(2);
    }

    private static Task<TraconTestHost> StartAsync(TwoNodeDatabase database)
        => TraconTestHost.StartAsync(configureTracon: database.Use);

    private static AgentDefinitionRequest Agent(string name, string instructions, float? temperature = null)
        => new()
        {
            Name = name,
            Instructions = instructions,
            Model = new ModelBinding { Provider = "echo", Model = "echo-1", Temperature = temperature },
        };

    private static async Task PostAsync<T>(TraconTestHost host, string path, T body)
    {
        using var response = await host.Client.PostAsJsonAsync(Api(path), body);

        response.EnsureSuccessStatusCode();
    }

    private static async Task PutAsync<T>(TraconTestHost host, string path, T body)
    {
        using var response = await host.Client.PutAsJsonAsync(Api(path), body);

        response.EnsureSuccessStatusCode();
    }

    private static async Task<JsonNode> ExportAsync(TraconTestHost host, string query = "")
    {
        using var response = await host.Client.GetAsync(Api("definitions/export" + query));

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    private static async Task<JsonNode> ImportAsync(TraconTestHost host, JsonNode bundle, bool dryRun)
    {
        using var response = await host.Client.PostAsync(
            Api($"definitions/import?dryRun={(dryRun ? "true" : "false")}"),
            new StringContent(bundle.ToJsonString(), Encoding.UTF8, "application/json"));
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);

        return JsonNode.Parse(text)!;
    }

    private static string Strip(JsonNode bundle)
    {
        var copy = bundle.DeepClone().AsObject();

        copy.Remove("exportedAt");

        return copy.ToJsonString();
    }
}
