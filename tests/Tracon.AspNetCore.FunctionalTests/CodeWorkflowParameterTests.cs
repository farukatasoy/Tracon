using System.Net;
using System.Net.Http.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.WorkflowResponseHarness;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Run parameters of a workflow defined in code (<c>WorkflowRegistrationOptions.Parameters</c>): the schema is
/// checked before the run opens, the values reach the function nodes and the agent steps, a queued run carries them, and a
/// run that is answered continues with the values it started with.
/// </summary>
/// <remarks>
/// <para>
/// Before this a code workflow's schema was empty and every value was refused as unknown, so the one honest way to hand a
/// production its identity - the id as a parameter, the nodes loading what they need - was a stored workflow only. The
/// graph here is the shape such a pipeline has: load (a function that reads the id), a human review, publish (a function
/// that reads the id again in the run that answers).
/// </para>
/// <para>
/// An agent step can read its version from a run parameter: the run that answers a human request runs the
/// version the run started with, not the one an edit made in between.
/// </para>
/// </remarks>
public sealed class CodeWorkflowParameterTests
{
    private const string Flow = "production";
    private const string Tone = "tone";

    [Fact]
    public async Task The_schema_a_registration_declares_is_listed_with_the_workflow()
    {
        await using var host = await StartAsync(new Probe());

        using var listed = await host.Client.GetAsync(new Uri("/tracon/api/workflows", UriKind.Relative));
        var workflow = (await TraconTestHost.ReadJsonAsync(listed)).EnumerateArray()
            .Single(static item => string.Equals(item.GetProperty("name").GetString(), Flow, StringComparison.Ordinal));

        workflow.GetProperty("parameters").EnumerateArray().Select(static parameter => parameter.GetProperty("name").GetString())
            .ShouldBe(["productionId", "language"]);
    }

    [Fact]
    public async Task A_function_node_reads_the_value_the_run_was_started_with()
    {
        var probe = new Probe();
        await using var host = await StartAsync(probe);

        var (runId, _) = await RunToAwaitAsync(host, "production-id=p-1");

        probe.Loaded.ShouldBe(["p-1/tr"]);
        (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId)).ShouldNotBeNull().Status.ShouldBe(RunStatus.AwaitingInput);
    }

    [Fact]
    public async Task A_name_the_registration_did_not_declare_is_a_400_before_any_node_runs()
    {
        var probe = new Probe();
        await using var host = await StartAsync(probe);

        using var response = await host.Client.PostAsJsonAsync(
            $"/tracon/api/workflows/{Flow}/run",
            new WorkflowRunHttpRequest { Message = "go", Parameters = Values(("productionId", "p-1"), ("producionId", "typo")) });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await TraconTestHost.ReadJsonAsync(response);

        body.GetProperty("unknownParameters").EnumerateArray().Select(static item => item.GetString()).ShouldBe(["producionId"]);
        probe.Loaded.ShouldBeEmpty();
        (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_required_value_nobody_gave_is_a_400_that_names_it()
    {
        await using var host = await StartAsync(new Probe());

        using var response = await host.Client.PostAsJsonAsync(
            $"/tracon/api/workflows/{Flow}/run",
            new WorkflowRunHttpRequest { Message = "go", Parameters = Values(("language", "tr")) });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("missingParameters").EnumerateArray()
            .Select(static item => item.GetString()).ShouldBe(["productionId"]);
    }

    [Fact]
    public async Task A_value_that_has_a_default_may_be_left_out()
    {
        var probe = new Probe();
        await using var host = await StartAsync(probe);

        await RunToAwaitAsync(host, "production-id=p-2", language: null);

        probe.Loaded.ShouldBe(["p-2/en"]);
    }

    [Fact]
    public async Task A_queued_run_carries_the_values_to_its_nodes()
    {
        var probe = new Probe();
        await using var host = await StartAsync(probe, runWorker: true);

        using var accepted = await QueuedWorkflowHarness.PostAsync(
            host,
            $"/api/workflows/{Flow}/run",
            new WorkflowRunHttpRequest { Message = "go", Parameters = Values(("productionId", "p-3"), ("language", "de")) },
            idempotencyKey: "queued-parameters");

        var runId = await QueuedWorkflowHarness.AcceptedRunIdAsync(accepted);

        await host.WaitForRunStatusAsync(runId, "AwaitingInput");

        probe.Loaded.ShouldBe(["p-3/de"]);
    }

    [Fact]
    public async Task The_run_that_answers_continues_under_the_values_the_run_started_with()
    {
        var probe = new Probe();
        await using var host = await StartAsync(probe);

        var (runId, requestId) = await RunToAwaitAsync(host, "production-id=p-4", language: "fr");

        // The answer carries no parameters of its own: it takes the recorded values.
        using var response = await RespondAsync(host, runId, new { requestId, approved = true });
        var frames = await FramesAsync(response);

        (await host.WaitForTerminalRunAsync(RunIdOf(frames))).Status.ShouldBe(RunStatus.Completed);
        probe.Published.ShouldBe(["p-4/fr"]);
    }

    [Fact]
    public async Task An_agent_step_receives_only_the_values_its_own_agent_declares()
    {
        var provider = new FakeModelProvider("tone-provider").WithModel(new ModelDescriptor { Name = "tone-model" }).EchoesUserMessage();
        var probe = new Probe();

        await using var host = await StartAsync(
            probe,
            configure: builder =>
            {
                builder.AddModelProvider(provider);
                builder.AddAgent(new AgentDefinition
                {
                    Name = "narrator",
                    Instructions = "Speak in a {{tone}} tone.",
                    Model = new ModelBinding { Provider = "tone-provider", Model = "tone-model" },
                    Parameters = [new AgentParameter { Name = Tone, Kind = AgentParameterKind.Text, DefaultValue = "plain" }],
                    Origin = AgentDefinitionOrigin.Code,
                });
                builder.AddWorkflow(
                    "narration",
                    static services =>
                    {
                        var step = services.GetWorkflowAgentStep("narration", "narrator");

                        return new WorkflowBuilder(step).WithOutputFrom(step).WithName("narration").Build();
                    },
                    static options => options.Parameters =
                    [
                        new AgentParameter { Name = "productionId", Kind = AgentParameterKind.Text, Required = true },
                        new AgentParameter { Name = Tone, Kind = AgentParameterKind.Text, DefaultValue = "plain" },
                    ]);
            });

        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/api/workflows/narration/run",
            new WorkflowRunHttpRequest { Message = "go", Parameters = Values(("productionId", "p-5"), (Tone, "formal")) });

        response.EnsureSuccessStatusCode();
        _ = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        // The agent's own parameter is bound; the workflow's other name does not reach it and does not fail it.
        provider.Requests.ShouldHaveSingleItem().Options!.Instructions.ShouldBe("Speak in a formal tone.");
    }

    [Theory]
    [InlineData("1", "ALPHA text", 1)]
    [InlineData(null, "GAMMA text", 3)]
    public async Task The_run_that_answers_runs_the_agent_version_the_run_started_with(string? startedWith, string expectedText, int expectedVersion)
    {
        // A number the run started with is kept through the wait. An empty value means "the current version", resolved
        // when the step runs, so an edit made while the run waited is the version its later step runs.
        var provider = new FakeModelProvider("episode-provider").WithModel(new ModelDescriptor { Name = "episode-model" }).EchoesUserMessage();

        await using var host = await StartAsync(
            new Probe(),
            configure: builder =>
            {
                builder.AddModelProvider(provider);

                builder.AddWorkflowFunction<List<ChatMessage>, string>("ask", _ => (_, _, _) => new ValueTask<string>("Write the episode?"));
                builder.AddWorkflowFunction<bool, List<ChatMessage>>(
                    "brief",
                    _ => (_, _, _) => new ValueTask<List<ChatMessage>>([new ChatMessage(ChatRole.User, "Write the episode.")]));

                builder.AddWorkflow(
                    "episode-review",
                    static services =>
                    {
                        var ask = services.GetWorkflowFunction("ask");
                        var review = RequestPort.Create<string, bool>(Port).BindAsExecutor(allowWrappedRequests: false);
                        var brief = services.GetWorkflowFunction("brief");
                        var writer = services.GetWorkflowAgentStep("episode-review", "writer", static step => step.AgentVersionParameter = "writerVersion");

                        return new WorkflowBuilder(ask)
                            .AddEdge(ask, review)
                            .AddEdge(review, brief)
                            .AddEdge(brief, writer)
                            .WithOutputFrom(writer)
                            .WithName("episode-review")
                            .Build();
                    },
                    static options => options.Parameters = [new AgentParameter { Name = "writerVersion", Kind = AgentParameterKind.Number }]);
            });

        await SaveWriterAsync(host, HttpMethod.Post, "/tracon/api/agents", "ALPHA text");
        await SaveWriterAsync(host, HttpMethod.Put, "/tracon/api/agents/writer", "BETA text");

        using var started = await host.Client.PostAsJsonAsync(
            "/tracon/api/workflows/episode-review/run",
            new WorkflowRunHttpRequest { Message = "go", Parameters = startedWith is null ? null : Values(("writerVersion", startedWith)) });

        started.StatusCode.ShouldBe(HttpStatusCode.OK, await started.Content.ReadAsStringAsync());

        var runId = RunIdOf((await SseReader.ReadAllAsync(await started.Content.ReadAsStreamAsync())).ToList());
        var requestId = (await RequestsAsync(host, runId)).ShouldHaveSingleItem();

        // The agent step has not run yet. An edit lands while the run waits for its answer.
        provider.Requests.ShouldBeEmpty();
        await SaveWriterAsync(host, HttpMethod.Put, "/tracon/api/agents/writer", "GAMMA text");

        // The answer carries no parameters of its own: it takes the recorded values.
        using var response = await RespondAsync(host, runId, new { requestId, approved = true });
        var frames = await FramesAsync(response);

        (await host.WaitForTerminalRunAsync(RunIdOf(frames))).Status.ShouldBe(RunStatus.Completed);
        provider.Requests.ShouldHaveSingleItem().Options!.Instructions.ShouldBe(expectedText);

        (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Where(static run => run.Kind == RunKind.Agent)
            .ShouldHaveSingleItem()
            .AgentVersion.ShouldBe(expectedVersion);
    }

    private static async Task SaveWriterAsync(TraconTestHost host, HttpMethod method, string path, string instructions)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(new AgentDefinitionRequest
            {
                Name = "writer",
                Instructions = instructions,
                Model = new ModelBinding { Provider = "episode-provider", Model = "episode-model" },
            }),
        };

        using var saved = await host.Client.SendAsync(request);

        saved.IsSuccessStatusCode.ShouldBeTrue(await saved.Content.ReadAsStringAsync());
    }

    private static Dictionary<string, string> Values(params (string Name, string Value)[] values)
        => values.ToDictionary(static value => value.Name, static value => value.Value, StringComparer.Ordinal);

    private static Task<TraconTestHost> StartAsync(Probe probe, bool runWorker = false, Action<ITraconBuilder>? configure = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.UseWorkflows();

                builder.AddWorkflowFunction<List<ChatMessage>, string>(
                    "load",
                    _ => (_, context, _) =>
                    {
                        var values = context.GetRunParameters();

                        probe.Loaded.Add($"{values["productionId"]}/{values["language"]}");

                        return new ValueTask<string>($"Approve production {values["productionId"]}?");
                    });

                builder.AddWorkflowFunction<bool, string>(
                    "publish",
                    _ => (_, context, _) =>
                    {
                        var values = context.GetRunParameters();

                        probe.Published.Add($"{values["productionId"]}/{values["language"]}");

                        return new ValueTask<string>("published");
                    });

                builder.AddWorkflow(
                    Flow,
                    static services =>
                    {
                        var load = services.GetWorkflowFunction("load");
                        var review = RequestPort.Create<string, bool>(Port).BindAsExecutor(allowWrappedRequests: false);
                        var publish = services.GetWorkflowFunction("publish");

                        return new WorkflowBuilder(load)
                            .AddEdge(load, review)
                            .AddEdge(review, publish)
                            .WithOutputFrom(publish)
                            .WithName(Flow)
                            .Build();
                    },
                    static options => options.Parameters =
                    [
                        new AgentParameter { Name = "productionId", Kind = AgentParameterKind.Text, Required = true },
                        new AgentParameter { Name = "language", Kind = AgentParameterKind.Text, DefaultValue = "en" },
                    ]);

                configure?.Invoke(builder);
            },
            configureServices: services => services.Configure<TraconSchedulingOptions>(options =>
            {
                options.PollInterval = TimeSpan.FromMilliseconds(50);
                options.RunWorker = runWorker;
            }));

    // "production-id=p-1" keeps the call sites short: the id is the only value that varies.
    private static async Task<(Guid RunId, string RequestId)> RunToAwaitAsync(TraconTestHost host, string production, string? language = "tr")
    {
        var id = production[(production.IndexOf('=', StringComparison.Ordinal) + 1)..];
        var values = language is null ? Values(("productionId", id)) : Values(("productionId", id), ("language", language));

        using var response = await host.Client.PostAsJsonAsync(
            $"/tracon/api/workflows/{Flow}/run",
            new WorkflowRunHttpRequest { Message = "go", Parameters = values });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var runId = RunIdOf((await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync())).ToList());

        return (runId, (await RequestsAsync(host, runId)).ShouldHaveSingleItem());
    }

    private sealed class Probe
    {
        public List<string> Loaded { get; } = [];

        public List<string> Published { get; } = [];
    }
}
