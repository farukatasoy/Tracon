using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Tests.Common;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Large inputs and many concurrent runs on the in-memory test host: each case ends and ends in a named outcome, never
/// in a hang, a 5xx or a run left <c>Running</c>.
/// </summary>
/// <remarks>
/// <para>
/// The bound on each case is a failure bound and not a measurement (<see cref="Generous"/>): it catches a case that
/// stops ending, and says nothing about one that gets slower. The sizes are the smaller of the two measured
/// sizes: a workflow of 90 agent steps, the longest chain the default super-step limit lets through (a chain of 2 000
/// used to be saved and took 11 to 23 seconds to fail; it is refused at the save now), and 128 tools, the most an agent may
/// carry (5 000 took about 1.5 seconds to compile and put 690 KB of tool definitions into every model request; an agent over
/// the limit does not compile).
/// </para>
/// <para>
/// What these tests do NOT assert is a limit on the session: a long one is sent to the model whole and its state grows with
/// every turn, which the context-window preflight and compaction bound when a host turns them on.
/// </para>
/// </remarks>
public sealed class ResourceLimitTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task A_workflow_with_more_agent_steps_than_the_child_run_budget_is_saved_and_its_run_ends_failed_at_the_budget()
    {
        // The most the default super-step limit (100) lets a chain of nodes have is 100; 90 leaves no doubt about the edge.
        const int Steps = 90;

        await using var host = await TraconTestHost.StartAsync(builder =>
        {
            for (var index = 0; index < Steps; index++)
            {
                builder.AddAgent(TestData.Definition($"step{index}"));
            }

            builder.UseWorkflows();
        });

        var nodes = Enumerable.Range(0, Steps).Select(static index => new { name = $"step{index}", kind = "Agent" }).ToArray();

        var running = Task.Run(
            async () =>
            {
                using var saved = await host.Client.PutAsJsonAsync("/tracon/api/workflows/long-chain", new { kind = "Sequential", nodes });

                saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

                using var response = await host.Client.PostAsJsonAsync("/tracon/api/workflows/long-chain/run", new WorkflowRunHttpRequest { Message = "hello" });
                _ = await response.Content.ReadAsStringAsync();

                return response.StatusCode;
            },
            TestContext.Current.CancellationToken);

        (await running.WaitAsync(Generous, TestContext.Current.CancellationToken)).ShouldBe(HttpStatusCode.OK);

        var runs = await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 10_000 });
        var root = runs.Single(static run => run.ParentRunId is null);

        // The tree budget stops the chain at the 26th step; without it the run would go on to the end of the chain.
        root.Status.ShouldBe(RunStatus.Failed);
        root.Error.ShouldNotBeNull().Type.ShouldBe("run_budget_exceeded");
        runs.Count(static run => run.ParentRunId is not null).ShouldBe(host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<TraconOptions>>().Value.AgentGraph.MaxTotalRuns);
    }

    [Fact]
    public async Task An_agent_with_as_many_tools_as_the_limit_allows_compiles_and_runs_and_the_model_gets_all_of_them()
    {
        const int Count = TraconToolOptions.DefaultMaxToolsPerAgent;

        var provider = new FakeModelProvider("probe").WithModel(new ModelDescriptor { Name = "probe-1" }).RespondsWith("ok");
        var names = Enumerable.Range(0, Count).Select(static index => $"tool_{index}").ToArray();

        await using var host = await TraconTestHost.StartAsync(builder =>
        {
            builder.AddModelProvider(provider);

            foreach (var name in names)
            {
                builder.AddTool(AIFunctionFactory.Create((string query) => query, name, $"The tool called {name}. It returns its argument."));
            }

            builder.AddAgent(new AgentDefinition
            {
                Name = "many-tools",
                Model = new ModelBinding { Provider = "probe", Model = "probe-1" },
                Instructions = "Reply briefly.",
                ToolNames = names,
                Origin = AgentDefinitionOrigin.Code,
            });
        });

        var first = await RunAsync(host, "many-tools", sessionId: null).WaitAsync(Generous, TestContext.Current.CancellationToken);

        first.ShouldBe(HttpStatusCode.OK);
        provider.Requests.Single().Options!.Tools!.Count.ShouldBe(Count);

        // The compiled agent is cached, and a second run ends the same way.
        (await RunAsync(host, "many-tools", sessionId: null).WaitAsync(Generous, TestContext.Current.CancellationToken)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_agent_with_more_tools_than_the_limit_does_not_compile_and_the_error_names_the_option()
    {
        const int Count = TraconToolOptions.DefaultMaxToolsPerAgent + 1;

        var provider = new FakeModelProvider("probe").WithModel(new ModelDescriptor { Name = "probe-1" }).RespondsWith("ok");
        var names = Enumerable.Range(0, Count).Select(static index => $"tool_{index}").ToArray();

        await using var host = await TraconTestHost.StartAsync(builder =>
        {
            builder.AddModelProvider(provider);

            foreach (var name in names)
            {
                builder.AddTool(AIFunctionFactory.Create((string query) => query, name, $"The tool called {name}. It returns its argument."));
            }

            builder.AddAgent(new AgentDefinition
            {
                Name = "too-many-tools",
                Model = new ModelBinding { Provider = "probe", Model = "probe-1" },
                Instructions = "Reply briefly.",
                ToolNames = names,
                Origin = AgentDefinitionOrigin.Code,
            });
        });

        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/api/agents/too-many-tools/run",
            new AgentRunRequest { Message = "hello" });
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.ShouldBeFalse(body);
        body.ShouldContain("Tracon:Tools:MaxToolsPerAgent");
        body.ShouldContain($"{Count} tools");
        provider.Requests.ShouldBeEmpty("no model was called for an agent that did not compile");
    }

    [Fact]
    public async Task An_agent_that_may_call_a_thousand_agents_is_saved_and_the_model_request_does_not_grow_with_them()
    {
        const int Count = 1_000;

        var provider = new FakeModelProvider("probe").WithModel(new ModelDescriptor { Name = "probe-1" }).RespondsWith("ok");

        await using var host = await TraconTestHost.StartAsync(builder =>
        {
            builder.AddModelProvider(provider);

            for (var index = 0; index < Count; index++)
            {
                builder.AddAgent(new AgentDefinition
                {
                    Name = $"helper{index}",
                    Description = $"The helper number {index}.",
                    Model = new ModelBinding { Provider = "probe", Model = "probe-1" },
                    Instructions = "Reply briefly.",
                    Origin = AgentDefinitionOrigin.Code,
                });
            }
        });

        var saveAndRun = Task.Run(
            async () =>
            {
                using var saved = await host.Client.PostAsJsonAsync(
                    "/tracon/api/agents",
                    new AgentDefinitionRequest
                    {
                        Name = "coordinator",
                        Instructions = "Delegate.",
                        Model = new ModelBinding { Provider = "probe", Model = "probe-1" },
                        CallableAgents = Enumerable.Range(0, Count).Select(static index => new CallableAgentReference { Name = $"helper{index}" }).ToArray(),
                    });

                saved.StatusCode.ShouldBe(HttpStatusCode.Created, await saved.Content.ReadAsStringAsync());

                return await RunAsync(host, "coordinator", sessionId: null);
            },
            TestContext.Current.CancellationToken);

        (await saveAndRun.WaitAsync(Generous, TestContext.Current.CancellationToken)).ShouldBe(HttpStatusCode.OK);

        // The callable agents are reached through a few generic tools, not one tool each.
        provider.Requests.Single().Options!.Tools!.Count.ShouldBeLessThan(20);
    }

    [Fact]
    public async Task A_session_with_twenty_thousand_prior_messages_runs_and_grows_by_the_new_turn_only()
    {
        const int Prior = 20_000;

        var provider = new FakeModelProvider("probe").WithModel(new ModelDescriptor { Name = "probe-1" }).RespondsWith("ok");

        await using var host = await TraconTestHost.StartAsync(builder =>
        {
            builder.AddModelProvider(provider);
            builder.AddAgent(new AgentDefinition
            {
                Name = "long-chat",
                Model = new ModelBinding { Provider = "probe", Model = "probe-1" },
                Instructions = "Reply briefly.",
                Origin = AgentDefinitionOrigin.Code,
            });
        });

        (await RunAsync(host, "long-chat", "long")).ShouldBe(HttpStatusCode.OK);

        // Stretch the one saved turn to twenty thousand messages by repeating it.
        var store = host.Services.GetRequiredService<ISessionStore>();
        var record = (await store.GetAsync("long"))!;
        await store.SaveAsync(record with { State = Repeat(record.State, Prior) });
        var before = (await store.GetAsync("long"))!.State.GetRawText().Length;

        (await RunAsync(host, "long-chat", "long").WaitAsync(Generous, TestContext.Current.CancellationToken)).ShouldBe(HttpStatusCode.OK);

        var after = (await store.GetAsync("long"))!.State.GetRawText().Length;

        // The state holds the prior messages and the new turn: nothing else is added per turn. There is no limit on its
        // size, and the whole history reaches the model (compaction is the agent's own opt-in).
        (after - before).ShouldBeLessThan(1_024);
        provider.Requests[^1].Messages.Count.ShouldBeGreaterThan(Prior);
    }

    [Fact]
    public async Task A_hundred_concurrent_runs_all_reach_a_terminal_state()
    {
        const int Runs = 100;

        var provider = new OverlappingProvider("slow");

        await using var host = await TraconTestHost.StartAsync(builder =>
        {
            builder.AddModelProvider(provider);
            builder.AddAgent(new AgentDefinition
            {
                Name = "worker",
                Model = new ModelBinding { Provider = "slow", Model = "slow-1" },
                Instructions = "Reply briefly.",
                Origin = AgentDefinitionOrigin.Code,
            });
        });

        var all = Task.WhenAll(Enumerable.Range(0, Runs).Select(index => RunAsync(host, "worker", $"session-{index}")));

        try
        {
            // The runs overlap by construction: every model call waits until all of them are in, and then they are let go.
            await WaitUntil.TrueAsync(() => provider.CountCalls() == Runs, "every run to be waiting in the model call at once");
        }
        finally
        {
            provider.Release();
        }

        var statuses = await all.WaitAsync(Generous, TestContext.Current.CancellationToken);

        statuses.ShouldAllBe(static status => status == HttpStatusCode.OK);

        var runs = await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 10_000 });

        runs.Count.ShouldBe(Runs);
        runs.ShouldAllBe(static run => run.Status == RunStatus.Completed, "a run was left in a state other than Completed");
    }

    private static async Task<HttpStatusCode> RunAsync(TraconTestHost host, string agent, string? sessionId)
    {
        using var response = await host.Client.PostAsJsonAsync(
            $"/tracon/api/agents/{agent}/run",
            new AgentRunRequest { Message = "hello", SessionId = sessionId });
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldNotContain("event: error", Case.Sensitive);

        return response.StatusCode;
    }

    private static JsonElement Repeat(JsonElement state, int target)
    {
        var node = JsonNode.Parse(state.GetRawText())!;
        var messages = FindMessages(node).ShouldNotBeNull("the session state holds no message list");
        var original = messages.Select(static item => item!.DeepClone()).ToList();

        messages.Clear();

        for (var index = 0; index < target; index++)
        {
            messages.Add(original[index % original.Count].DeepClone());
        }

        return JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
    }

    private static JsonArray? FindMessages(JsonNode node)
    {
        switch (node)
        {
            case JsonArray array when array.Count > 0 && array[0] is JsonObject first && first.ContainsKey("role"):
                return array;
            case JsonArray array:
                return array.Where(static item => item is not null).Select(static item => FindMessages(item!)).FirstOrDefault(static found => found is not null);
            case JsonObject obj:
                return obj.Where(static property => property.Value is not null).Select(static property => FindMessages(property.Value!)).FirstOrDefault(static found => found is not null);
            default:
                return null;
        }
    }

    /// <summary>A model provider whose calls all wait for one release, so that many runs overlap.</summary>
    private sealed class OverlappingProvider(string name) : IModelProvider
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public string Name { get; } = name;

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = $"{name}-1" }];

        /// <summary>Counts the model calls that are waiting or have been let go.</summary>
        /// <returns>The number of calls made so far.</returns>
        public int CountCalls() => Volatile.Read(ref _calls);

        public IChatClient CreateChatClient(ModelBinding binding) => new Client(this);

        /// <summary>Lets every waiting call and every later one answer.</summary>
        public void Release() => _release.TrySetResult();

        private sealed class Client(OverlappingProvider owner) : IChatClient
        {
            public async Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref owner._calls);
                await owner._release.Task.WaitAsync(cancellationToken);

                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"));
            }

            public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref owner._calls);
                await owner._release.Task.WaitAsync(cancellationToken);

                yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
            }

            public object? GetService(Type serviceType, object? serviceKey = null) => null;

            public void Dispose()
            {
                // Nothing to release.
            }
        }
    }
}
