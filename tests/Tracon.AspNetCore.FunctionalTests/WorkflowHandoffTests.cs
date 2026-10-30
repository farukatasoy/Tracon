using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// Same rationale as ToolGovernanceEndpointTests.cs: the package's
// Tracon.Testing.TraconTestHost and this project's own TraconTestHost
// share the same name; a blanket `using Tracon.Testing;` would produce CS0104.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A stored <see cref="WorkflowKind.Handoff"/> workflow hands off to the agent the model names.
/// </summary>
/// <remarks>
/// Microsoft Agent Framework's handoff step gives its agent the handoff tools and the handoff
/// instructions on the run options, not on the agent (<c>ChatClientAgentRunOptions</c>). A
/// workflow step is Tracon's sub-agent wrapper, and the wrapper built its own options and dropped
/// the ones it was given: the model never saw a handoff tool, so no workflow ever handed off
/// (measured 2026-10-20). With the tools passed on, the handoff call still came back under the
/// child agent's own id, and the handoff step took it for an outside tool call and waited for its
/// result. Earlier tests checked that a handoff workflow compiles and fails on time, not that it
/// hands off.
/// </remarks>
public sealed class WorkflowHandoffTests
{
    private const string WorkflowName = "help-desk";
    private const string HandoffInstructions = "HANDOFF-RULE: hand technical questions to the expert.";

    [Fact]
    public async Task The_model_sees_the_handoff_tools_and_the_named_agent_answers()
    {
        var triage = new FakeModelProvider("triage-model").ForModel("triage-1", static cfg => cfg.CallsTool("handoff_to_1").RespondsWith("triage answered"));
        var expert = new FakeModelProvider("expert-model").ForModel("expert-1", static cfg => cfg.RespondsWith("expert answered"));

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(triage)
            .AddModelProvider(expert)
            .AddAgent(Agent("triage", "triage-model", "triage-1", "Routes the question."))
            .AddAgent(Agent("expert", "expert-model", "expert-1", "Answers technical questions."))
            .UseWorkflows());

        using (var saved = await host.Client.PutAsJsonAsync(
                   $"/tracon/api/workflows/{WorkflowName}",
                   new WorkflowSaveRequest
                   {
                       Kind = WorkflowKind.Handoff,
                       AgentNames = ["triage", "expert"],
                       HandoffInstructions = HandoffInstructions,
                   }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        string body;

        using (var response = await host.Client.PostAsJsonAsync(
                   $"/tracon/api/workflows/{WorkflowName}/run",
                   new WorkflowRunHttpRequest { Message = "my build fails" }))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            body = await response.Content.ReadAsStringAsync();
        }

        triage.Requests.ShouldNotBeEmpty();
        var first = triage.Requests[0].Options.ShouldNotBeNull();
        (first.Tools ?? []).Any(static tool => string.Equals(tool.Name, "handoff_to_1", StringComparison.Ordinal))
            .ShouldBeTrue("the triage model never saw a handoff tool");
        first.Instructions.ShouldNotBeNull().ShouldContain(HandoffInstructions, Case.Sensitive);
        expert.Requests.ShouldNotBeEmpty("the workflow never handed off to the expert");
        body.ShouldContain("expert answered");

        var runs = await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery { Take = 20, OnlyRootRuns = false });
        runs.Single(static run => run.Kind == RunKind.Workflow).Status.ShouldBe(RunStatus.Completed);
    }

    private static AgentDefinition Agent(string name, string provider, string model, string description)
        => new()
        {
            Name = name,
            Description = description,
            Instructions = $"You are the {name}.",
            Model = new ModelBinding { Provider = provider, Model = model },
            Origin = AgentDefinitionOrigin.Code,
        };
}
