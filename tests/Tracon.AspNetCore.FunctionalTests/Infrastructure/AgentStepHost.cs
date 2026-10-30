using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// The host of the tests that look at an agent step through both of its ways in: a stored chain (an agent node, then a
/// function node) and a graph built in code (<c>GetWorkflowAgentStep</c>), over the same agent.
/// </summary>
/// <remarks>
/// Both ways run the same executor, so a test that runs each proves the executor and not one door to it.
/// </remarks>
internal static class AgentStepHost
{
    /// <summary>The agent both workflows run.</summary>
    public const string Agent = "writer";

    /// <summary>The stored chain: <see cref="Agent"/>, then a function node.</summary>
    public const string Stored = "stored-chain";

    /// <summary>The graph built in code: <see cref="Agent"/> as a plain step.</summary>
    public const string Code = "code-step";

    /// <summary>The provider's name.</summary>
    public const string Provider = "step-provider";

    /// <summary>The model's name.</summary>
    public const string Model = "step-model";

    /// <summary>A provider with a priced model.</summary>
    /// <returns>The provider.</returns>
    public static FakeModelProvider PricedProvider()
        => new FakeModelProvider(Provider)
            .WithModel(new ModelDescriptor
            {
                Name = Model,
                InputCostPerMillionTokens = 1_000m,
                OutputCostPerMillionTokens = 2_000m,
                SupportsStructuredOutput = true,
            });

    /// <summary>An agent defined in code that runs on the provider.</summary>
    /// <param name="format">The response format; <see langword="null"/> for free text.</param>
    /// <param name="tools">The tools the agent may call.</param>
    /// <returns>The definition.</returns>
    public static AgentDefinition WriterAgent(AgentResponseFormat? format = null, params string[] tools)
        => new()
        {
            Name = Agent,
            Instructions = "Write.",
            Model = new ModelBinding { Provider = Provider, Model = Model, ResponseFormat = format },
            ToolNames = tools,
            Origin = AgentDefinitionOrigin.Code,
        };

    /// <summary>Starts a host with both workflows.</summary>
    /// <param name="provider">The model provider.</param>
    /// <param name="agent">The agent; <see cref="WriterAgent"/> when <see langword="null"/>.</param>
    /// <param name="configure">More Tracon configuration.</param>
    /// <param name="configureServices">More service registrations.</param>
    /// <returns>The host, with the stored chain already saved.</returns>
    public static async Task<TraconTestHost> StartAsync(
        FakeModelProvider provider,
        AgentDefinition? agent = null,
        Action<ITraconBuilder>? configure = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(provider);
                builder.AddAgent(agent ?? WriterAgent());
                builder.UseWorkflows();

                builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "after",
                    static _ => static (messages, _, _) => new ValueTask<List<ChatMessage>>(messages));

                builder.AddWorkflow(
                    Code,
                    static services =>
                    {
                        var step = services.GetWorkflowAgentStep(Code, Agent);

                        return new WorkflowBuilder(step).WithOutputFrom(step).WithName(Code).Build();
                    });

                configure?.Invoke(builder);
            },
            configureServices: configureServices);

        using var saved = await host.Client.PutAsJsonAsync(
            $"/tracon/api/workflows/{Stored}",
            new WorkflowSaveRequest
            {
                Kind = WorkflowKind.Sequential,
                Nodes =
                [
                    new WorkflowNodeReference { Name = Agent, Kind = WorkflowNodeKind.Agent },
                    new WorkflowNodeReference { Name = "after", Kind = WorkflowNodeKind.Function },
                ],
            });

        saved.EnsureSuccessStatusCode();

        return host;
    }

    /// <summary>Runs a workflow over the streamed endpoint and waits for its row to settle.</summary>
    /// <param name="host">The host.</param>
    /// <param name="workflow">The workflow.</param>
    /// <returns>The workflow's run row.</returns>
    public static async Task<RunRecord> RunAsync(TraconTestHost host, string workflow)
    {
        using var response = await host.Client.PostAsJsonAsync(
            $"/tracon/api/workflows/{workflow}/run",
            new WorkflowRunHttpRequest { Message = "Episode 12" });

        response.EnsureSuccessStatusCode();

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());
        var accepted = frames.First(static frame => string.Equals(frame.Event, "run", StringComparison.Ordinal));
        var runId = JsonDocument.Parse(accepted.Data).RootElement.GetProperty("runId").GetGuid();

        return await host.WaitForTerminalRunAsync(runId);
    }

    /// <summary>The child rows of a workflow run: its agent steps.</summary>
    /// <param name="host">The host.</param>
    /// <param name="rootRunId">The workflow's run.</param>
    /// <returns>The rows.</returns>
    public static async Task<IReadOnlyList<RunRecord>> StepRowsAsync(TraconTestHost host, Guid rootRunId)
        => (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Where(run => run.ParentRunId == rootRunId)
            .ToList();

    /// <summary>A run's event stream as the raw text of its frames.</summary>
    /// <param name="host">The host.</param>
    /// <param name="runId">The run.</param>
    /// <returns>The frames' text.</returns>
    public static async Task<string> EventsAsync(TraconTestHost host, Guid runId)
        => await host.Client.GetStringAsync(new Uri($"/tracon/api/runs/{runId}/events", UriKind.Relative));
}
