using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 222 (F-588): the extension names of a definition are checked when it is validated and
/// when it is saved, with the path of the offending list item; the extension list endpoint
/// shows what the host registered and runs none of it.
/// </summary>
public sealed class AgentExtensionValidationTests
{
    private static readonly Uri Validate = new("/tracon/api/agents/validate", UriKind.Relative);
    private static readonly Uri Agents = new("/tracon/api/agents", UriKind.Relative);
    private static readonly Uri List = new("/tracon/api/agent-extensions", UriKind.Relative);

    [Fact]
    public async Task An_unknown_and_a_repeated_name_are_reported_with_the_path_of_their_list_item()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Validate, Request(["policy", "missing", "policy"]));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var report = await TraconTestHost.ReadJsonAsync(response);

        report.GetProperty("valid").GetBoolean().ShouldBeFalse();

        var messages = report.GetProperty("messages").EnumerateArray()
            .Select(static message => (
                Code: message.GetProperty("code").GetString(),
                Path: message.GetProperty("path").GetString()))
            .ToList();

        messages.ShouldContain(("unknown_extension", "extensionNames[1]"));
        messages.ShouldContain(("duplicate_extension", "extensionNames[2]"));
    }

    [Fact]
    public async Task A_registered_name_validates_and_an_empty_list_is_valid()
    {
        await using var host = await StartAsync();

        foreach (var names in new[] { new[] { "policy" }, [], new[] { "policy", "audit" } })
        {
            using var response = await host.Client.PostAsJsonAsync(Validate, Request(names));
            var report = await TraconTestHost.ReadJsonAsync(response);

            report.GetProperty("valid").GetBoolean().ShouldBeTrue(report.ToString());
        }
    }

    [Fact]
    public async Task Saving_a_definition_with_an_unknown_extension_name_is_refused_and_a_known_one_is_stored()
    {
        await using var host = await StartAsync();

        using (var refused = await host.Client.PostAsJsonAsync(Agents, Request(["missing"])))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await refused.Content.ReadAsStringAsync()).ShouldContain("agent extension 'missing' is not registered");
        }

        using (var saved = await host.Client.PostAsJsonAsync(Agents, Request(["policy"])))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.Created, await saved.Content.ReadAsStringAsync());
        }

        using var read = await host.Client.GetAsync(new Uri("/tracon/api/agents/ext-validated", UriKind.Relative));
        var detail = await TraconTestHost.ReadJsonAsync(read);

        detail.GetProperty("definition").GetProperty("extensionNames").EnumerateArray().Select(static name => name.GetString()).ShouldBe(["policy"]);
        detail.GetProperty("descriptor").GetProperty("extensionNames").EnumerateArray().Select(static name => name.GetString()).ShouldBe(["policy"]);
    }

    [Fact]
    public async Task Validating_a_definition_that_names_an_extension_reports_an_extension_that_overreaches()
    {
        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddAgentExtension("greedy", static context => context.ChatAgentOptions!.ChatOptions!.ModelId = "other"));

        using var response = await host.Client.PostAsJsonAsync(Validate, Request(["greedy"]));
        var report = await TraconTestHost.ReadJsonAsync(response);

        report.GetProperty("valid").GetBoolean().ShouldBeFalse();
        report.GetProperty("messages").EnumerateArray()
            .Select(static message => message.GetProperty("code").GetString())
            .ShouldContain("compilation_error", StringComparer.Ordinal);
    }

    [Fact]
    public async Task The_list_returns_every_registered_extension_ordered_by_name_and_runs_none()
    {
        var calls = 0;
        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddAgentExtension("zeta", _ => Interlocked.Increment(ref calls), "Last one.")
            .AddAgentExtension("alpha", _ => Interlocked.Increment(ref calls)));

        using var response = await host.Client.GetAsync(List);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var items = (await TraconTestHost.ReadJsonAsync(response)).EnumerateArray().ToList();

        items.Select(static item => item.GetProperty("name").GetString()).ShouldBe(["alpha", "zeta"]);
        items[0].TryGetProperty("description", out var none).ShouldBeTrue();
        none.ValueKind.ShouldBe(JsonValueKind.Null);
        items[1].GetProperty("description").GetString().ShouldBe("Last one.");
        calls.ShouldBe(0);
    }

    [Fact]
    public async Task The_list_is_empty_not_an_error_when_nothing_is_registered()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(List);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(response)).GetArrayLength().ShouldBe(0);
    }

    private static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(builder => builder
            .AddAgentExtension("policy", static _ => { }, "Adds the house policy.")
            .AddAgentExtension("audit", static _ => { }));

    private static AgentDefinitionRequest Request(IReadOnlyList<string> extensionNames)
        => new()
        {
            Name = "ext-validated",
            Instructions = "Answer briefly.",
            Model = TestData.Model(),
            ExtensionNames = extensionNames,
        };
}
