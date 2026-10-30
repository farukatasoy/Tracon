using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Tracon.AGUI.FunctionalTests.Infrastructure;
using Tracon.Testing;

namespace Tracon.AGUI.FunctionalTests;

/// <summary>
/// The tools a front end declares never become the tools a model can call.
/// </summary>
/// <remarks>
/// A tool is defined in code. The request says only which client-side tools this front end can RUN: the schema, the name
/// and the existence of a tool are the code's. These pin that boundary at the HTTP surface, where the attack would arrive.
/// </remarks>
public sealed class AGUIToolBoundaryTests
{
    [Fact]
    public async Task A_tool_the_agent_does_not_carry_never_reaches_the_model_and_the_run_records_its_name()
    {
        var model = new FakeModelProvider("boundary-model").EchoesUserMessage();
        await using var host = await AGUITestHost.StartAsync(builder => builder
            .AddModelProvider(model)
            .AddAgent(TestAgents.Definition(TestAgents.Echo, "boundary-model")));

        var response = await host.RunAsync(
            TestAgents.Echo,
            AGUIInputs.User("t-evil", "hi", "r1", AGUIInputs.Tool("steal_files"), AGUIInputs.Tool("delete_everything")));

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        response.Text.ShouldBe("Echo: hi");

        var offered = model.Requests.SelectMany(static request => request.Options?.Tools ?? []).Select(static tool => tool.Name).ToList();
        offered.ShouldNotContain("steal_files", StringComparer.Ordinal);
        offered.ShouldNotContain("delete_everything", StringComparer.Ordinal);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var notices = new List<RunEvent>();

        await foreach (var runEvent in runs.ReadEventsAsync(response.TraconRunId, 0, TestContext.Current.CancellationToken))
        {
            if (string.Equals(runEvent.CustomType, RunEventCustomTypes.AGUIToolsIgnored, StringComparison.Ordinal))
            {
                notices.Add(runEvent);
            }
        }

        var notice = notices.ShouldHaveSingleItem();
        var names = JsonDocument.Parse(notice.Payload!).RootElement.GetProperty("tools").EnumerateArray().Select(static name => name.GetString()).ToList();
        names.ShouldBe(["delete_everything", "steal_files"]);
        notice.Payload!.ShouldNotContain("declared by the front end", Case.Sensitive, "the notice carries names, never a description or a schema");
    }

    [Fact]
    public async Task The_schema_a_request_declares_never_replaces_the_one_in_code()
    {
        FakeModelProvider? model = null;
        await using var host = await AGUITestHost.StartAsync(builder => model = TestAgents.AddClientTool(builder));

        const string Hostile = """{"type":"object","properties":{"command":{"type":"string"}},"required":["command"]}""";

        var response = await host.RunAsync(
            TestAgents.ClientTool,
            AGUIInputs.User("t-schema", "open it", "r1", AGUIInputs.Tool(TestAgents.OpenPage, Hostile)));

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);

        var tool = model!.Requests[0].Options!.Tools!.OfType<AIFunctionDeclaration>()
            .Single(static t => string.Equals(t.Name, TestAgents.OpenPage, StringComparison.Ordinal));
        var properties = tool.JsonSchema.GetProperty("properties");

        properties.TryGetProperty("url", out _).ShouldBeTrue("the code's schema is what the model saw");
        properties.TryGetProperty("command", out _).ShouldBeFalse("the request's schema is never used");
    }

    [Fact]
    public async Task A_client_tool_the_front_end_does_not_declare_is_refused_and_named()
    {
        // The model sees a client-side tool because the agent's definition names it. A tool the front end cannot run
        // would be called and then wait for a result nobody can produce, and a run cannot hide a tool of its definition
        // from the model (K-1039). So the request is refused instead.
        await using var host = await AGUITestHost.StartAsync(builder => TestAgents.AddClientTool(builder));

        var response = await host.RunAsync(TestAgents.ClientTool, AGUIInputs.User("t-missing", "open it"));

        response.Status.ShouldBe(HttpStatusCode.BadRequest, response.Body);
        response.Problem.GetProperty("title").GetString().ShouldBe("Client tools not declared");
        response.Problem.GetProperty("detail").GetString()!.ShouldContain(TestAgents.OpenPage, Case.Sensitive);

        // Refused before the run began: no run, no session.
        var sessions = host.Services.GetRequiredService<ISessionStore>();
        (await sessions.GetAsync("t-missing", TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task A_caller_who_may_not_run_the_agent_learns_nothing_about_its_tools()
    {
        // The tool check names the agent's client-side tools. It runs AFTER the gates, so a refused caller never sees them.
        await using var host = await AGUITestHost.StartAsync(
            builder => TestAgents.AddClientTool(builder),
            configureServices: services => services.AddSingleton<IRunAuthorizationHandler>(new DenyAllHandler()));

        var response = await host.RunAsync(TestAgents.ClientTool, AGUIInputs.User("t-denied", "open it"));

        response.Status.ShouldBe(HttpStatusCode.Forbidden, response.Body);
        response.Body.ShouldNotContain(TestAgents.OpenPage, Case.Sensitive);
    }

    [Fact]
    public async Task A_client_tool_of_the_experiment_arm_is_checked_even_when_the_current_version_does_not_carry_it()
    {
        // The check read the latest definition while the run took the arm's version. The arm carries the tool,
        // the front end did not declare it: the run would call it and wait for a result nobody can produce.
        await using var host = await StartExperimentAsync(armCarriesTool: true);

        var response = await host.RunAsync(VersionedAgent, AGUIInputs.User("t-arm", "open it"));

        response.Status.ShouldBe(HttpStatusCode.BadRequest, response.Body);
        response.Problem.GetProperty("title").GetString().ShouldBe("Client tools not declared");
        response.Problem.GetProperty("detail").GetString()!.ShouldContain(TestAgents.OpenPage, Case.Sensitive);
    }

    [Fact]
    public async Task A_client_tool_only_the_current_version_carries_does_not_refuse_a_run_of_the_arm_that_lacks_it()
    {
        await using var host = await StartExperimentAsync(armCarriesTool: false);

        var response = await host.RunAsync(VersionedAgent, AGUIInputs.User("t-arm-free", "hi"));

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        response.Text.ShouldBe("Echo: hi");
    }

    private const string VersionedAgent = "versioned-agent";

    // Version 1 is the experiment's only arm, version 2 is the current one; exactly one of them carries the client tool.
    private static async Task<AGUITestHost> StartExperimentAsync(bool armCarriesTool)
    {
        var model = new FakeModelProvider("arm-model").EchoesUserMessage();
        var host = await AGUITestHost.StartAsync(builder => builder
            .AddModelProvider(model)
            .AddClientTool(
                TestAgents.OpenPage,
                "Opens a page in the user's browser.",
                JsonDocument.Parse(TestAgents.OpenPageSchema).RootElement.Clone()));

        try
        {
            var store = host.Services.GetRequiredService<IAgentDefinitionStore>();

            await store.SaveAsync(
                TestAgents.Definition(VersionedAgent, "arm-model", armCarriesTool ? [TestAgents.OpenPage] : []),
                TestContext.Current.CancellationToken);
            await store.SaveAsync(
                TestAgents.Definition(VersionedAgent, "arm-model", armCarriesTool ? [] : [TestAgents.OpenPage]),
                TestContext.Current.CancellationToken);

            using var created = await host.Client.PutAsJsonAsync(
                new Uri("/tracon/api/experiments/arm-tools", UriKind.Relative),
                new
                {
                    agentName = VersionedAgent,
                    variants = new[] { new { name = "control", version = 1, weight = 100 } },
                },
                TestContext.Current.CancellationToken);
            created.EnsureSuccessStatusCode();

            using var started = await host.Client.PostAsJsonAsync(
                new Uri("/tracon/api/experiments/arm-tools/start", UriKind.Relative),
                new { },
                TestContext.Current.CancellationToken);
            started.EnsureSuccessStatusCode();

            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    private sealed class DenyAllHandler : IRunAuthorizationHandler
    {
        public ValueTask<RunAuthorizationResult> AuthorizeRunAsync(RunAuthorizationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(RunAuthorizationResult.Deny("not for you"));

        public ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(SessionAuthorizationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(RunAuthorizationResult.Deny("not for you"));
    }
}
