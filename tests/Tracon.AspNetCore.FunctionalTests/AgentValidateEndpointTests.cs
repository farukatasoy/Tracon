using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// F-60 — <c>POST /api/agents/validate</c>: validates a definition without
/// saving it and without calling any model.
/// </summary>
public sealed class AgentValidateEndpointTests
{
    private static readonly Uri Validate = new("/tracon/api/agents/validate", UriKind.Relative);
    private static readonly Uri Agents = new("/tracon/api/agents", UriKind.Relative);
    private static readonly Uri Runs = new("/tracon/api/runs", UriKind.Relative);

    [Fact]
    public async Task Valid_definition_returns_200_and_valid_true()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Validate, TestData.Request());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await TraconTestHost.ReadJsonAsync(response);
        json.GetProperty("valid").GetBoolean().ShouldBeTrue();
        json.GetProperty("inconclusive").GetBoolean().ShouldBeFalse();
        json.GetProperty("messages").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Unknown_tool_returns_200_and_valid_false()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            Validate,
            TestData.Request() with { ToolNames = ["nonexistent_tool"] });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await TraconTestHost.ReadJsonAsync(response);
        json.GetProperty("valid").GetBoolean().ShouldBeFalse();

        var messages = json.GetProperty("messages").EnumerateArray().ToArray();

        messages.Select(static message => message.GetProperty("code").GetString())
            .ToArray()
            .ShouldBe(["unknown_tool"]);

        // Severity travels as a NAME, not as an ordinal. An ordinal round-trips
        // through the generated client and looks green in a serialization test,
        // but the curl output a human reads would say "1".
        var severity = messages[0].GetProperty("severity");
        severity.ValueKind.ShouldBe(JsonValueKind.String);
        severity.GetString().ShouldBe("Error");
    }

    [Fact]
    public async Task Self_calling_definition_is_invalid_with_the_cycle_code()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            Validate,
            TestData.Request() with { CallableAgentNames = ["db-agent"] });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await TraconTestHost.ReadJsonAsync(response);
        json.GetProperty("valid").GetBoolean().ShouldBeFalse();
        json.GetProperty("messages")[0].GetProperty("code").GetString().ShouldBe("cycle");
    }

    [Fact]
    public async Task Empty_name_returns_400()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Validate, TestData.Request() with { Name = "" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reader_cannot_use_the_endpoint_operator_can()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(TraconPolicies.Reader, static policy => policy.RequireAssertion(static _ => true))
                .AddPolicy(TraconPolicies.Operator, static policy => policy.RequireAssertion(static _ => false)));

        using var forbidden = await host.Client.PostAsJsonAsync(Validate, TestData.Request());
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Catalog_and_runs_are_unchanged_after_validation()
    {
        await using var host = await TraconTestHost.StartAsync();

        var beforeAgents = (await TraconTestHost.ReadJsonAsync(await host.Client.GetAsync(Agents))).GetArrayLength();
        var beforeRuns = (await TraconTestHost.ReadJsonAsync(await host.Client.GetAsync(Runs))).GetArrayLength();

        using var valid = await host.Client.PostAsJsonAsync(Validate, TestData.Request());
        valid.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var invalid = await host.Client.PostAsJsonAsync(
            Validate,
            TestData.Request() with { ToolNames = ["nonexistent_tool"] });
        invalid.StatusCode.ShouldBe(HttpStatusCode.OK);

        var afterAgents = (await TraconTestHost.ReadJsonAsync(await host.Client.GetAsync(Agents))).GetArrayLength();
        var afterRuns = (await TraconTestHost.ReadJsonAsync(await host.Client.GetAsync(Runs))).GetArrayLength();

        afterAgents.ShouldBe(beforeAgents);
        afterRuns.ShouldBe(beforeRuns);
    }
}
