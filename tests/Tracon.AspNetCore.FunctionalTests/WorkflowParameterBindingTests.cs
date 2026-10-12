using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A workflow run carries parameter values to its agent steps.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 F-294: until now only <c>POST /api/agents/{name}/run</c> bound parameter
/// values. A workflow step resolved its agent from the catalog, so a step's
/// instructions always carried the agent's defaults. The cases run through the
/// real host because the binding crosses DI, the ambient run scope, the MAF
/// graph and the run store, and a wrong layer compiles and passes a unit test:
/// a bound step that skipped the decorator chain would answer correctly and
/// leave no row behind (K-581).
/// </para>
/// </remarks>
public sealed class WorkflowParameterBindingTests
{
    private const string Writer = "writer";
    private const string Editor = "editor";
    private const string ModelName = "param-model";
    private const string Workflow = "podcast";

    [Fact]
    public async Task A_workflow_value_reaches_each_steps_instructions()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        await SaveWorkflowAsync(host);

        var run = await RunAsync(host, Workflow, new WorkflowRunHttpRequest
        {
            Message = "Episode 12",
            Parameters = Values(("tone", "formal")),
        });

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);

        var instructions = provider.Requests.Select(static request => request.Options!.Instructions).ToList();

        // writer declares only 'tone'; the workflow's 'voice' is dropped for it.
        instructions.ShouldContain("Write the script in a formal tone.", StringComparer.Ordinal);

        // editor declares 'tone' and 'voice'. The workflow default for 'voice'
        // ('alloy') beats the editor's own default ('plain'), and the supplied
        // 'tone' beats the editor's default ('casual').
        instructions.ShouldContain("Edit for a formal tone, voice alloy.", StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_step_keeps_its_own_default_for_a_name_the_workflow_does_not_declare()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        await SaveWorkflowAsync(host, workflowParameters: [Tone(required: true)]);

        var run = await RunAsync(host, Workflow, new WorkflowRunHttpRequest
        {
            Message = "Episode 12",
            Parameters = Values(("tone", "formal")),
        });

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);

        // The workflow declares no 'voice': editor binds its own default.
        provider.Requests.Select(static request => request.Options!.Instructions)
            .ShouldContain("Edit for a formal tone, voice plain.", StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_bound_step_still_leaves_its_run_row_and_records_its_values()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        await SaveWorkflowAsync(host);

        var run = await RunAsync(host, Workflow, new WorkflowRunHttpRequest
        {
            Message = "Episode 12",
            Parameters = Values(("tone", "formal")),
        });

        // 🚨 K-581: a parameterized step bypasses the catalog and so the
        // decorator chain. Without the hand-applied pipeline the answer is the
        // same and the tree has no children.
        var steps = (await host.Services.GetRequiredService<IRunStore>()
                .QueryRunsAsync(new RunQuery { Take = 50, OnlyRootRuns = false }))
            .Where(candidate => candidate.ParentRunId == run.Id)
            .OrderBy(static candidate => candidate.StartedAt)
            .ToList();

        steps.Select(static step => step.AgentName).ShouldBe([Writer, Editor]);
        steps.ShouldAllBe(static step => step.Status == RunStatus.Completed);

        var inputs = host.Services.GetRequiredService<IRunInputStore>();
        var tenant = host.Services.GetRequiredService<ITenantContext>().TenantId;

        // The workflow row records the FINAL set: the supplied tone and the
        // schema default of 'voice'.
        var workflowRecord = await inputs.GetAsync(tenant, run.Id);

        workflowRecord.ShouldNotBeNull();
        workflowRecord.Parameters.ShouldNotBeNull();
        workflowRecord.Parameters.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => $"{pair.Key}={pair.Value}")
            .ShouldBe(["tone=formal", "voice=alloy"]);

        // Each step records only what ITS schema declared.
        var writerRecord = await inputs.GetAsync(tenant, steps[0].Id);

        writerRecord.ShouldNotBeNull();
        writerRecord.Parameters.ShouldNotBeNull().ShouldContainKeyAndValue("tone", "formal");
        writerRecord.Parameters.ShouldNotContainKey("voice");

        var editorRecord = await inputs.GetAsync(tenant, steps[1].Id);

        editorRecord.ShouldNotBeNull();
        editorRecord.Parameters.ShouldNotBeNull().Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_workflow_without_parameters_runs_exactly_as_before()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        await SaveWorkflowAsync(host, workflowParameters: []);

        var run = await RunAsync(host, Workflow, new WorkflowRunHttpRequest { Message = "Episode 12" });

        // The agents' own schemas (writer's 'tone' is required) mean the step
        // fails on the guard, as it did before this phase: no workflow schema,
        // no coverage check, the catalog path.
        run.Status.ShouldBe(RunStatus.Failed);
        provider.Requests.ShouldBeEmpty();

        var inputs = host.Services.GetRequiredService<IRunInputStore>();
        var tenant = host.Services.GetRequiredService<ITenantContext>().TenantId;

        (await inputs.GetAsync(tenant, run.Id)).ShouldBeNull("a workflow with no schema records nothing");
    }

    [Fact]
    public async Task Two_runs_of_one_workflow_with_different_values_never_share_an_instruction()
    {
        // The compiled agent cache has no notion of a value; the bound step
        // must bypass it. Two overlapping runs would show the first run's tone
        // in the second if a compiled agent were shared.
        var provider = Provider();
        await using var host = await StartAsync(provider);

        await SaveWorkflowAsync(host);

        var first = RunAsync(host, Workflow, new WorkflowRunHttpRequest
        {
            Message = "one",
            Parameters = Values(("tone", "formal")),
        });

        var second = RunAsync(host, Workflow, new WorkflowRunHttpRequest
        {
            Message = "two",
            Parameters = Values(("tone", "casual")),
        });

        await Task.WhenAll(first, second);

        var writerCalls = provider.Requests
            .Select(static request => request.Options!.Instructions)
            .Where(static text => text!.StartsWith("Write the script", StringComparison.Ordinal))
            .OrderBy(static text => text, StringComparer.Ordinal)
            .ToList();

        writerCalls.ShouldBe(["Write the script in a casual tone.", "Write the script in a formal tone."]);
    }

    [Fact]
    public async Task An_agent_a_step_calls_as_a_tool_receives_no_workflow_values()
    {
        // The workflow's values go to the DIRECT steps only: a sub-agent called
        // from a step gets none, exactly as an agent called from a direct run.
        const string writerModel = "writer-model";
        const string helperModel = "helper-model";

        var provider = new FakeModelProvider("param-provider")
            .WithModel(new ModelDescriptor { Name = writerModel })
            .WithModel(new ModelDescriptor { Name = helperModel })
            .ForModel(writerModel, cfg => cfg
                .CallsTool("background_agents_start_task", new { agentName = "helper", input = "sub task", description = "sub task" })
                .CallsTool("background_agents_wait_for_first_completion", new { taskIds = new[] { 1 } })
                .CallsTool("background_agents_get_task_results", new { taskId = 1 })
                .EchoesLastToolResult("Delegated: ", inputTokens: 4, outputTokens: 6))
            .ForModel(helperModel, cfg => cfg.RespondsWith("helped", inputTokens: 4, outputTokens: 6));

        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder
                    .AddModelProvider(provider)
                    .AddAgent(Definition(Writer, "Write the script in a {{tone}} tone.", Tone(required: true)) with
                    {
                        Model = new ModelBinding { Provider = "param-provider", Model = writerModel },
                        CallableAgentNames = ["helper"],
                    })
                    .AddAgent(Definition("helper", "Help in a {{tone}} way.", Tone(required: false, defaultValue: "kind")) with
                    {
                        Model = new ModelBinding { Provider = "param-provider", Model = helperModel },
                        Description = "Helps.",
                    })
                    .UseWorkflows();
            });

        await SaveWorkflowAsync(host, agentNames: [Writer], workflowParameters: [Tone(required: true)]);

        var run = await RunAsync(host, Workflow, new WorkflowRunHttpRequest
        {
            Message = "Episode 12",
            Parameters = Values(("tone", "formal")),
        });

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);

        var instructions = provider.Requests.Select(static request => request.Options!.Instructions).ToList();

        // The delegation really happened (the sub-agent was asked), and it bound
        // its OWN default, not the workflow's 'formal'.
        instructions.ShouldContain("Help in a kind way.", StringComparer.Ordinal);
        instructions.ShouldNotContain("Help in a formal way.", StringComparer.Ordinal);
    }

    internal static AgentDefinition Definition(string name, string instructions, params AgentParameter[] parameters) => new()
    {
        Name = name,
        Instructions = instructions,
        Parameters = parameters,
        Model = new ModelBinding { Provider = "param-provider", Model = ModelName },
        Origin = AgentDefinitionOrigin.Code,
    };

    internal static AgentParameter Tone(bool required, string? defaultValue = null) => new()
    {
        Name = "tone",
        Kind = AgentParameterKind.Text,
        Required = required,
        DefaultValue = defaultValue,
    };

    internal static FakeModelProvider Provider()
        => new FakeModelProvider("param-provider")
            .WithModel(new ModelDescriptor { Name = ModelName })
            .EchoesUserMessage();

    internal static IReadOnlyDictionary<string, string> Values(params (string Name, string Value)[] pairs)
        => pairs.ToDictionary(static pair => pair.Name, static pair => pair.Value, StringComparer.Ordinal);

    internal static Task<TraconTestHost> StartAsync(FakeModelProvider provider, Action<ITraconBuilder>? configure = null)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder
                    .AddModelProvider(provider)
                    .AddAgent(Definition(Writer, "Write the script in a {{tone}} tone.", Tone(required: true)))
                    .AddAgent(Definition(
                        Editor,
                        "Edit for a {{tone}} tone, voice {{voice}}.",
                        Tone(required: false, defaultValue: "casual"),
                        new AgentParameter { Name = "voice", Kind = AgentParameterKind.Text, DefaultValue = "plain" }))
                    .UseWorkflows();

                configure?.Invoke(builder);
            });

    internal static async Task SaveWorkflowAsync(
        TraconTestHost host,
        IReadOnlyList<string>? agentNames = null,
        IReadOnlyList<AgentParameter>? workflowParameters = null,
        string name = Workflow)
    {
        using var saved = await host.Client.PutAsJsonAsync(
            $"/tracon/api/workflows/{name}",
            new WorkflowSaveRequest
            {
                Kind = WorkflowKind.Sequential,
                AgentNames = agentNames ?? [Writer, Editor],
                Parameters = workflowParameters ??
                [
                    Tone(required: true),
                    new AgentParameter { Name = "voice", Kind = AgentParameterKind.Text, DefaultValue = "alloy" },
                ],
            });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    /// <summary>Runs a workflow over HTTP, reads the stream to its end and returns the root row.</summary>
    internal static async Task<RunRecord> RunAsync(TraconTestHost host, string name, WorkflowRunHttpRequest body)
    {
        using var response = await host.Client.PostAsJsonAsync($"/tracon/api/workflows/{name}/run", body);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());
        var accepted = frames.First(static frame => string.Equals(frame.Event, "run", StringComparison.Ordinal));
        var runId = JsonDocument.Parse(accepted.Data).RootElement.GetProperty("runId").GetGuid();

        return (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId))!;
    }
}
