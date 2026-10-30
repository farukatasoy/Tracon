using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// Tracon.Testing.TraconTestHost and this project's own host share a name, so the package is imported one
// type at a time.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;
using FakeModelRequest = Tracon.Testing.FakeModelRequest;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A stored Handoff workflow hands off along the edges its definition names, and a loop of handoffs
/// stops at the run's limit.
/// </summary>
/// <remarks>
/// <para>
/// Every case runs a real workflow over HTTP with a fake model per agent. A unit test of the compiler cannot show
/// the three things that matter: which handoff tools a model is OFFERED (the framework decides that from the edges),
/// that the answer comes from the agent a handoff returned to, and that the run stops where the counter says (the
/// counter lives in the step wrapper, below the framework).
/// </para>
/// <para>
/// A tool named <c>handoff_to_N</c> hands off to the N-th edge out of the calling agent, counting from 1 in the order
/// the definition lists them.
/// </para>
/// </remarks>
public sealed class WorkflowHandoffTopologyTests
{
    private const string Front = "frontline";
    private const string Billing = "billing";
    private const string Technical = "technical";

    [Fact]
    public async Task A_specialist_hands_back_to_the_coordinator_and_the_last_answer_is_the_coordinators()
    {
        var front = Model("front-model", static cfg => cfg.CallsTool("handoff_to_1").RespondsWith("The coordinator closes the case."));
        var billing = Model("billing-model", static cfg => cfg.CallsTool("handoff_to_1").RespondsWith("never used"));

        await using var host = await StartAsync(front, billing);

        await SaveAsync(host, "triage", new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Handoff,
            AgentNames = [Front, Billing],
            Handoffs = [Edge(Front, Billing), Edge(Billing, Front)],
        });

        var body = await RunAsync(host, "triage", "my invoice is wrong");

        // The framework goes on asking an agent that answered without a handoff, up to the turn limit of the pattern
        // (autonomous mode), so the coordinator's call count is not fixed: it is at least the two that matter.
        front.Requests.Count.ShouldBeGreaterThanOrEqualTo(2, "the coordinator handed off, then received the conversation back");
        billing.Requests.Count.ShouldBe(1);
        body.ShouldContain("The coordinator closes the case.", Case.Sensitive);

        var run = await ReadRunAsync(host, "triage");

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);
    }

    [Fact]
    public async Task Each_agent_is_offered_exactly_the_handoffs_of_its_edges_and_an_agent_without_an_edge_out_is_offered_none()
    {
        var front = Model("front-model", static cfg => cfg.CallsTool("handoff_to_2"));
        var billing = Model("billing-model", static cfg => cfg.RespondsWith("billing answered"));
        var technical = Model("technical-model", static cfg => cfg.RespondsWith("technical answered"));

        await using var host = await StartAsync(front, billing, technical);

        await SaveAsync(host, "triage", new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Handoff,
            AgentNames = [Front, Billing, Technical],
            Handoffs = [Edge(Front, Billing), Edge(Front, Technical, "Technical questions."), Edge(Billing, Front)],
        });

        var body = await RunAsync(host, "triage", "my build fails");

        HandoffTools(front.Requests[0]).ShouldBe(["handoff_to_1", "handoff_to_2"], ignoreOrder: true);
        billing.Requests.ShouldBeEmpty("the second edge of the coordinator leads to the technical agent");
        technical.Requests.ShouldNotBeEmpty("the workflow never handed off to the second edge");
        HandoffTools(technical.Requests[0]).ShouldBeEmpty("the technical agent has no edge out of it");
        body.ShouldContain("technical answered", Case.Sensitive);
    }

    [Fact]
    public async Task The_reason_of_an_edge_is_what_the_model_reads_as_the_description_of_the_handoff()
    {
        var front = Model("front-model", static cfg => cfg.RespondsWith("done"));
        var billing = Model("billing-model", static cfg => cfg.RespondsWith("billing answered"));

        await using var host = await StartAsync(front, billing);

        await SaveAsync(host, "triage", new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Handoff,
            AgentNames = [Front, Billing],
            Handoffs = [Edge(Front, Billing, "The invoice question is a billing matter.")],
        });

        await RunAsync(host, "triage", "my invoice is wrong");

        var tool = front.Requests[0].Options!.Tools!.Single(static tool => tool.Name.StartsWith("handoff_to_", StringComparison.Ordinal));

        tool.Description.ShouldBe("The invoice question is a billing matter.");
    }

    [Fact]
    public async Task Two_agents_that_hand_back_and_forth_end_the_run_failed_at_the_limit_with_the_stable_error_type()
    {
        var front = Model("front-model", static cfg => cfg.CallsTool("handoff_to_1").CallsTool("handoff_to_1").CallsTool("handoff_to_1"));
        var billing = Model("billing-model", static cfg => cfg.CallsTool("handoff_to_1").CallsTool("handoff_to_1").CallsTool("handoff_to_1"));

        await using var host = await StartAsync(front, billing);

        await SaveAsync(host, "loop", new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Handoff,
            AgentNames = [Front, Billing],
            Handoffs = [Edge(Front, Billing), Edge(Billing, Front)],
            MaxHandoffs = 2,
        });

        await RunAsync(host, "loop", "round and round");

        var run = await ReadRunAsync(host, "loop");

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull().Type.ShouldBe(TraconErrorTypes.HandoffLimitExceeded);
        run.Error.Class.ShouldBe(RunErrorClass.QuotaExceeded);
        run.Error.Message.ShouldContain("'maxHandoffs'", Case.Sensitive);

        // The third handoff is the one that goes past the limit of 2. It is stopped before the framework acts on it,
        // so the models were called three times in all: the coordinator twice and the specialist once.
        (front.Requests.Count + billing.Requests.Count).ShouldBe(3);
    }

    [Fact]
    public async Task A_definition_that_sets_no_limit_stops_at_ten_handoffs()
    {
        var front = Model("front-model", static cfg => cfg.CallsTool("handoff_to_1").EchoesLastToolResult());
        var billing = Model("billing-model", static cfg => cfg.CallsTool("handoff_to_1").EchoesLastToolResult());

        // A queue that is drained falls back to echoing, which is not a handoff: fill both models with eleven calls.
        for (var index = 0; index < 11; index++)
        {
            front.CallsTool("handoff_to_1");
            billing.CallsTool("handoff_to_1");
        }

        await using var host = await StartAsync(front, billing);

        await SaveAsync(host, "loop", new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Handoff,
            AgentNames = [Front, Billing],
            Handoffs = [Edge(Front, Billing), Edge(Billing, Front)],
        });

        await RunAsync(host, "loop", "round and round");

        var run = await ReadRunAsync(host, "loop");

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull().Type.ShouldBe(TraconErrorTypes.HandoffLimitExceeded);
        (front.Requests.Count + billing.Requests.Count).ShouldBe(11, "ten handoffs are allowed, the eleventh is the one that is stopped");
    }

    [Fact]
    public async Task Two_runs_in_flight_at_once_each_keep_their_own_count()
    {
        // Two workflows, two pairs of agents, one limit of three: each run makes two handoffs. A count shared by the
        // runs would reach four and stop one of them.
        var frontA = Model("front-a", static cfg => cfg.CallsTool("handoff_to_1").RespondsWith("A done"));
        var billingA = Model("billing-a", static cfg => cfg.CallsTool("handoff_to_1"));
        var frontB = Model("front-b", static cfg => cfg.CallsTool("handoff_to_1").RespondsWith("B done"));
        var billingB = Model("billing-b", static cfg => cfg.CallsTool("handoff_to_1"));

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(frontA).AddModelProvider(billingA).AddModelProvider(frontB).AddModelProvider(billingB)
            .AddAgent(Agent("front-a", frontA)).AddAgent(Agent("billing-a", billingA))
            .AddAgent(Agent("front-b", frontB)).AddAgent(Agent("billing-b", billingB))
            .UseWorkflows());

        foreach (var side in new[] { "a", "b" })
        {
            await SaveAsync(host, $"flow-{side}", new WorkflowSaveRequest
            {
                Kind = WorkflowKind.Handoff,
                AgentNames = [$"front-{side}", $"billing-{side}"],
                Handoffs = [Edge($"front-{side}", $"billing-{side}"), Edge($"billing-{side}", $"front-{side}")],
                MaxHandoffs = 3,
            });
        }

        await Task.WhenAll(RunAsync(host, "flow-a", "go"), RunAsync(host, "flow-b", "go"));

        (await ReadRunAsync(host, "flow-a")).Status.ShouldBe(RunStatus.Completed);
        (await ReadRunAsync(host, "flow-b")).Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task Two_runs_of_the_same_workflow_in_flight_at_once_each_count_their_own_handoff()
    {
        // One handoff each and a limit of one. The coordinator hands off and the specialist, which has no edge out,
        // answers, so the queue of the coordinator holds exactly one handoff for each run whichever run asks first. A
        // count shared by the two runs, or kept by the workflow, would reach two and stop one of them.
        var front = Model("front-model", static cfg => cfg.CallsTool("handoff_to_1").CallsTool("handoff_to_1"));
        var billing = Model("billing-model", static cfg => cfg.RespondsWith("billing answered"));

        await using var host = await StartAsync(front, billing);

        await SaveAsync(host, "triage", new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Handoff,
            AgentNames = [Front, Billing],
            Handoffs = [Edge(Front, Billing)],
            MaxHandoffs = 1,
        });

        await Task.WhenAll(RunAsync(host, "triage", "one"), RunAsync(host, "triage", "two"));

        var runs = await ReadRunsAsync(host, "triage");

        runs.Count.ShouldBe(2);
        runs.ShouldAllBe(static run => run.Status == RunStatus.Completed);
    }

    [Fact]
    public async Task The_four_fields_are_saved_and_read_back_and_a_save_without_them_clears_them()
    {
        await using var host = await StartAsync(Model("front-model", static cfg => cfg.RespondsWith("x")), Model("billing-model", static cfg => cfg.RespondsWith("y")));

        await SaveAsync(host, "triage", new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Handoff,
            AgentNames = [Front, Billing],
            Handoffs = [Edge(Front, Billing, "Billing questions."), Edge(Billing, Front)],
            MaxHandoffs = 6,
        });

        var stored = await ReadDefinitionAsync(host, "triage");

        Edges(stored).ShouldBe([(Front, Billing, "Billing questions."), (Billing, Front, null)]);
        OptionalInt(stored, "maxHandoffs").ShouldBe(6);

        await SaveAsync(host, "triage", new WorkflowSaveRequest { Kind = WorkflowKind.Handoff, AgentNames = [Front, Billing] });

        var cleared = await ReadDefinitionAsync(host, "triage");

        Edges(cleared).ShouldBeEmpty("a save replaces the whole definition, so the star comes back");
        OptionalInt(cleared, "maxHandoffs").ShouldBeNull();
    }

    [Fact]
    public async Task The_magentic_limits_are_saved_and_read_back()
    {
        await using var host = await StartAsync(Model("front-model", static cfg => cfg.RespondsWith("x")), Model("billing-model", static cfg => cfg.RespondsWith("y")));

        await SaveAsync(host, "plan", new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Magentic,
            ManagerAgentName = Front,
            AgentNames = [Billing],
            MaxStalls = 2,
            MaxResets = 1,
        });

        var stored = await ReadDefinitionAsync(host, "plan");

        OptionalInt(stored, "maxStalls").ShouldBe(2);
        OptionalInt(stored, "maxResets").ShouldBe(1);
    }

    [Theory]
    [InlineData("""{"kind":"Concurrent","agentNames":["frontline","billing"],"handoffs":[{"from":"frontline","to":"billing"}]}""", "'handoffs'")]
    [InlineData("""{"kind":"Sequential","agentNames":["frontline","billing"],"maxHandoffs":3}""", "'maxHandoffs'")]
    [InlineData("""{"kind":"Handoff","agentNames":["frontline","billing"],"maxStalls":3}""", "'maxStalls'")]
    [InlineData("""{"kind":"Handoff","agentNames":["frontline","billing"],"maxResets":1}""", "'maxResets'")]
    [InlineData("""{"kind":"Handoff","agentNames":["frontline","billing"],"handoffs":[{"from":"frontline","to":"support"}]}""", "handoffs[0] to 'support' is not in agentNames")]
    [InlineData("""{"kind":"Handoff","agentNames":["frontline","billing"],"handoffs":[{"from":"billing","to":"billing"}]}""", "hands 'billing' off to itself")]
    [InlineData("""{"kind":"Handoff","agentNames":["frontline","billing"],"handoffs":[{"from":"frontline","to":"billing"},{"from":"frontline","to":"billing"}]}""", "repeats the edge")]
    [InlineData("""{"kind":"Handoff","agentNames":["frontline","billing"],"handoffs":[{"from":"billing","to":"frontline"}]}""", "the first agent 'frontline'")]
    [InlineData("""{"kind":"Handoff","agentNames":["frontline","billing","technical"],"handoffs":[{"from":"frontline","to":"billing"}]}""", "agent 'technical' cannot be reached")]
    [InlineData("""{"kind":"Handoff","agentNames":["frontline","billing"],"handoffs":[{"from":"frontline","to":"billing","reason":"  "}]}""", "blank 'reason'")]
    [InlineData("""{"kind":"Handoff","agentNames":["frontline","billing"],"maxHandoffs":0}""", "'maxHandoffs' value must be positive")]
    [InlineData("""{"kind":"Magentic","managerAgentName":"frontline","agentNames":["billing"],"maxStalls":0}""", "'maxStalls' value must be positive")]
    [InlineData("""{"kind":"Magentic","managerAgentName":"frontline","agentNames":["billing"],"maxResets":0}""", "'maxResets' value must be positive")]
    public async Task A_definition_the_rules_refuse_is_a_400_that_names_the_field_or_the_edge(string body, string expected)
    {
        await using var host = await StartAsync(Model("front-model", static cfg => cfg.RespondsWith("x")), Model("billing-model", static cfg => cfg.RespondsWith("y")));

        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using var response = await host.Client.PutAsync(new Uri("/tracon/api/workflows/bad", UriKind.Relative), content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain(expected, Case.Sensitive);
    }

    private static FakeModelProvider Model(string name, Action<FakeModelProvider> script)
    {
        var provider = new FakeModelProvider(name);
        script(provider);

        return provider;
    }

    private static AgentDefinition Agent(string name, FakeModelProvider provider)
        => new()
        {
            Name = name,
            Description = $"The {name} agent.",
            Instructions = $"You are the {name}.",
            Model = new ModelBinding { Provider = provider.Name, Model = "fake-model" },
            Origin = AgentDefinitionOrigin.Code,
        };

    /// <summary>Starts a host with the coordinator, the billing agent and (given a third provider) the technical one.</summary>
    private static Task<TraconTestHost> StartAsync(FakeModelProvider front, FakeModelProvider billing, FakeModelProvider? technical = null)
        => TraconTestHost.StartAsync(builder =>
        {
            builder.AddModelProvider(front).AddModelProvider(billing);
            builder.AddAgent(Agent(Front, front)).AddAgent(Agent(Billing, billing));

            if (technical is not null)
            {
                builder.AddModelProvider(technical);
                builder.AddAgent(Agent(Technical, technical));
            }

            builder.UseWorkflows();
        });

    private static WorkflowHandoff Edge(string from, string to, string? reason = null)
        => new() { From = from, To = to, Reason = reason };

    private static async Task SaveAsync(TraconTestHost host, string name, WorkflowSaveRequest request)
    {
        using var saved = await host.Client.PutAsJsonAsync(new Uri($"/tracon/api/workflows/{name}", UriKind.Relative), request);

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    private static async Task<string> RunAsync(TraconTestHost host, string name, string message)
    {
        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/workflows/{name}/run", UriKind.Relative),
            new WorkflowRunHttpRequest { Message = message });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<JsonElement> ReadDefinitionAsync(TraconTestHost host, string name)
    {
        using var response = await host.Client.GetAsync(new Uri($"/tracon/api/workflows/{name}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await TraconTestHost.ReadJsonAsync(response);
    }

    private static int? OptionalInt(JsonElement definition, string property)
        => definition.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    private static List<(string From, string To, string? Reason)> Edges(JsonElement definition)
        => [.. definition.GetProperty("handoffs").EnumerateArray().Select(static edge => (
            edge.GetProperty("from").GetString()!,
            edge.GetProperty("to").GetString()!,
            edge.TryGetProperty("reason", out var reason) ? reason.GetString() : null))];

    private static async Task<IReadOnlyList<RunRecord>> ReadRunsAsync(TraconTestHost host, string workflow)
    {
        var runs = await host.Services.GetRequiredService<IRunStore>()
            .QueryRunsAsync(new RunQuery { Take = 50, OnlyRootRuns = false });

        return [.. runs.Where(run => run.Kind == RunKind.Workflow && string.Equals(run.AgentName, workflow, StringComparison.Ordinal))];
    }

    private static async Task<RunRecord> ReadRunAsync(TraconTestHost host, string workflow)
        => (await ReadRunsAsync(host, workflow)).ShouldHaveSingleItem();

    private static List<string> HandoffTools(FakeModelRequest request)
        => [.. (request.Options?.Tools ?? [])
            .Select(static tool => tool.Name)
            .Where(static name => name.StartsWith("handoff_to_", StringComparison.Ordinal))];
}
