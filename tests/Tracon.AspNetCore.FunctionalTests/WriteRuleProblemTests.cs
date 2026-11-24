using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A save endpoint that refuses a definition says why with a stable code, the way an import report line does.
/// </summary>
/// <remarks>
/// The title and the detail are prose and change. A client that has to tell "the model provider is unknown" from "a
/// tool is unknown" reads <c>code</c>, and the path of the field and every finding of the rule come with it. The
/// four save families share one writer of the problem, so one case each proves the writer reaches every family.
/// </remarks>
public sealed class WriteRuleProblemTests
{
    private static readonly Uri Agents = new("/tracon/api/agents", UriKind.Relative);

    [Fact]
    public async Task An_agent_created_with_an_unknown_model_provider_answers_with_the_code_of_the_rule_and_each_finding()
    {
        await using var host = await TraconTestHost.StartAsync();

        var request = TestData.Request() with { Model = new ModelBinding { Provider = "no-such-provider", Model = "any" } };
        using var response = await host.Client.PostAsJsonAsync(Agents, request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());

        var problem = await TraconTestHost.ReadJsonAsync(response);

        problem.GetProperty("title").GetString().ShouldBe("Definition invalid");
        problem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("no-such-provider");
        problem.GetProperty("code").GetString().ShouldBe("agent_definition_invalid");

        var messages = problem.GetProperty("messages").EnumerateArray().ToArray();

        messages.ShouldNotBeEmpty();
        messages.ShouldAllBe(static message => message.GetProperty("code").GetString()!.Length > 0);
        messages.ShouldAllBe(static message => message.GetProperty("message").GetString()!.Length > 0);
        messages.ShouldContain(static message => string.Equals(message.GetProperty("code").GetString(), "unknown_model", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_agent_updated_with_an_unknown_model_provider_answers_the_same_way()
    {
        await using var host = await TraconTestHost.StartAsync();

        using (var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request()))
        {
            created.EnsureSuccessStatusCode();
        }

        var request = TestData.Request() with { Model = new ModelBinding { Provider = "no-such-provider", Model = "any" } };
        using var response = await host.Client.PutAsJsonAsync(new Uri("/tracon/api/agents/db-agent", UriKind.Relative), request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());

        var problem = await TraconTestHost.ReadJsonAsync(response);

        problem.GetProperty("code").GetString().ShouldBe("agent_definition_invalid");
        problem.GetProperty("messages").EnumerateArray().ShouldContain(static message =>
            string.Equals(message.GetProperty("code").GetString(), "unknown_model", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_refusal_of_one_field_names_the_field()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Agents, TestData.Request(name: "team/support"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await TraconTestHost.ReadJsonAsync(response);

        problem.GetProperty("code").GetString().ShouldBe("agent_name_invalid");
        problem.GetProperty("path").GetString().ShouldBe("name");

        var single = problem.GetProperty("messages").EnumerateArray().ShouldHaveSingleItem();

        single.GetProperty("code").GetString().ShouldBe("agent_name_invalid");
        single.GetProperty("path").GetString().ShouldBe("name");
    }

    [Fact]
    public async Task A_workflow_that_names_a_function_nobody_registered_answers_with_its_code()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await PutJsonAsync(host, "workflows/broken", """{"kind":"Sequential","nodes":[{"name":"no-such-function","kind":"Function"}]}""");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());

        var problem = await TraconTestHost.ReadJsonAsync(response);

        problem.GetProperty("code").GetString().ShouldBe("workflow_definition_invalid");
        problem.GetProperty("messages").EnumerateArray().ShouldHaveSingleItem()
            .GetProperty("code").GetString().ShouldBe("workflow_definition_invalid");
    }

    [Fact]
    public async Task A_skill_the_rules_refuse_answers_with_its_code()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await PutJsonAsync(host, "skills/empty", """{"name":"empty","description":"","instructions":"Read the policy."}""");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("code").GetString().ShouldBe("skill_invalid");
    }

    [Fact]
    public async Task An_mcp_server_the_rules_refuse_answers_with_its_code()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await PutJsonAsync(host, "mcp-servers/bad", """{"endpoint":"ftp://example.test/mcp"}""");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());

        var problem = await TraconTestHost.ReadJsonAsync(response);

        problem.GetProperty("code").GetString().ShouldBe("mcp_server_invalid");
        problem.GetProperty("messages").EnumerateArray().ShouldHaveSingleItem();
    }

    /// <summary>A refusal that is not a 400 carries its code too: the name is taken.</summary>
    [Fact]
    public async Task A_name_that_is_taken_answers_409_with_its_code()
    {
        await using var host = await TraconTestHost.StartAsync();

        using (var first = await host.Client.PostAsJsonAsync(Agents, TestData.Request()))
        {
            first.EnsureSuccessStatusCode();
        }

        using var second = await host.Client.PostAsJsonAsync(Agents, TestData.Request());

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await TraconTestHost.ReadJsonAsync(second)).GetProperty("code").GetString().ShouldBe("agent_name_in_use");
    }

    /// <summary>
    /// The dry run of a save already answered with a report; the import report and the save answer name the same
    /// finding by the same code.
    /// </summary>
    [Fact]
    public async Task The_save_and_the_validate_endpoint_name_the_same_code_for_the_same_finding()
    {
        await using var host = await TraconTestHost.StartAsync();

        var request = TestData.Request() with { Model = new ModelBinding { Provider = "no-such-provider", Model = "any" } };

        using var save = await host.Client.PostAsJsonAsync(Agents, request);
        using var validate = await host.Client.PostAsJsonAsync(new Uri("/tracon/api/agents/validate", UriKind.Relative), request);

        var saved = await TraconTestHost.ReadJsonAsync(save);
        var validated = await TraconTestHost.ReadJsonAsync(validate);

        var savedCodes = saved.GetProperty("messages").EnumerateArray().Select(static message => message.GetProperty("code").GetString()).Order(StringComparer.Ordinal);
        var validatedCodes = validated.GetProperty("messages").EnumerateArray()
            .Where(static message => string.Equals(message.GetProperty("severity").GetString(), "Error", StringComparison.OrdinalIgnoreCase))
            .Select(static message => message.GetProperty("code").GetString())
            .Order(StringComparer.Ordinal);

        savedCodes.ShouldBe(validatedCodes);
    }

    private static async Task<HttpResponseMessage> PutJsonAsync(TraconTestHost host, string path, string json)
        => await host.Client.PutAsync(
            new Uri($"/tracon/api/{path}", UriKind.Relative),
            new StringContent(json, Encoding.UTF8, "application/json"));
}
