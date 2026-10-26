using System.Net;
using System.Net.Http.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

// MAAI001: AgentFileStore is marked "evaluation purposes only" - the same rationale as
// SessionWorkspaceGovernanceTests.
#pragma warning disable MAAI001

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A tool call does not start once the call tree's token, cost or time budget is spent.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 F-530: the tree budget refused a model call, a new agent step and a function node, and a
/// tool call started anyway. When one model turn asked for two paid tools and the first passed
/// the ceiling, the second still ran: its money and its side effect were gone, and the model
/// call that would have read its result was already refused. The gate sits in the tool wrapper
/// chain and reads the budget from the AMBIENT run scope, inside the tool-call loop of
/// Microsoft Agent Framework; only the real host shows that the scope and the refusal both
/// survive that hop.
/// </para>
/// <para>
/// Every scenario runs one turn that calls two tools in order. The first tool spends the
/// ceiling; the second must not start. The run then ends at its next model call, as it did
/// before the gate.
/// </para>
/// </remarks>
public sealed class ToolBudgetGateTests
{
    private const string Provider = "gate-provider";
    private const string Model = "gate-model";
    private const string Agent = "narrator";
    private const string First = "speak_first";
    private const string Second = "speak_second";

    // --- The three boundaries the gate stops at ---

    [Fact]
    public async Task A_tool_does_not_start_once_the_cost_budget_is_spent()
    {
        var tools = new ToolBodies();

        await using var host = await StartAsync(
            tools.Speaks(First, amount: 5m),
            tools.Counts(Second),
            options => options.AgentGraph.MaxTotalCost = 1m);

        await RunUntilRefusedAsync(host);

        tools.Count(First).ShouldBe(1);
        tools.Count(Second).ShouldBe(0, "the refused tool's body was called");

        var refused = await RefusedCallAsync(host);

        refused.Error.ShouldNotBeNull().ShouldContain("cost budget is exhausted", Case.Sensitive);
        refused.Error.ShouldContain("Tracon:AgentGraph:MaxTotalCost", Case.Sensitive);
        refused.Error.ShouldContain("The tool call did not start.", Case.Sensitive);
    }

    [Fact]
    public async Task A_tool_does_not_start_once_the_token_budget_is_spent()
    {
        var tools = new ToolBodies();

        // Only a model call spends tokens, and a tool that calls a model does it (K-893). The
        // first tool stands for one: it charges the tree 1000 tokens against a ceiling of 500.
        await using var host = await StartAsync(
            tools.Run(First, () => TraconRunContext.Current!.Budget!.RecordUsage(1_000)),
            tools.Counts(Second),
            options => options.AgentGraph.MaxTotalTokens = 500);

        await RunUntilRefusedAsync(host);

        tools.Count(First).ShouldBe(1);
        tools.Count(Second).ShouldBe(0, "the refused tool's body was called");

        var refused = await RefusedCallAsync(host);

        refused.Error.ShouldNotBeNull().ShouldContain("token budget is exhausted", Case.Sensitive);
        refused.Error.ShouldContain("Tracon:AgentGraph:MaxTotalTokens", Case.Sensitive);
    }

    [Fact]
    public async Task A_tool_does_not_start_once_the_time_budget_has_passed()
    {
        var clock = new ManualTimeProvider();
        var tools = new ToolBodies();

        await using var host = await StartAsync(
            // The first tool takes ten seconds of a five-second budget.
            tools.Run(First, () => clock.Advance(TimeSpan.FromSeconds(10))),
            tools.Counts(Second),
            options => options.AgentGraph.MaxDuration = TimeSpan.FromSeconds(5),
            services => services.AddSingleton<TimeProvider>(clock));

        await RunUntilRefusedAsync(host);

        tools.Count(First).ShouldBe(1);
        tools.Count(Second).ShouldBe(0, "the refused tool's body was called");

        var refused = await RefusedCallAsync(host);

        refused.Error.ShouldNotBeNull().ShouldContain("time budget is exhausted", Case.Sensitive);
        refused.Error.ShouldContain("Tracon:AgentGraph:MaxDuration", Case.Sensitive);
    }

    // --- What the refusal looks like ---

    [Fact]
    public async Task A_refused_call_is_recorded_as_failed_and_the_run_ends_with_the_budget_identity()
    {
        var tools = new ToolBodies();

        await using var host = await StartAsync(
            tools.Speaks(First, amount: 5m),
            tools.Counts(Second),
            options => options.AgentGraph.MaxTotalCost = 1m);

        await RunUntilRefusedAsync(host);

        var run = await RunRecordAsync(host);

        // The same identity a refused model call ends with: the gate adds no new way to fail.
        run.Status.ShouldBe(RunStatus.Failed);

        var error = run.Error.ShouldNotBeNull();

        error.Type.ShouldBe(TraconRunBudgetExceededException.RunBudgetExceededErrorType);
        error.Class.ShouldBe(RunErrorClass.QuotaExceeded);

        var calls = await host.Services.GetRequiredService<IRunStore>().ListToolInvocationsAsync(run.Id);

        calls.Count.ShouldBe(2, "the refused call is a row of its own");

        var first = calls.Single(static call => string.Equals(call.ToolName, First, StringComparison.Ordinal));
        var second = calls.Single(static call => string.Equals(call.ToolName, Second, StringComparison.Ordinal));

        first.Succeeded.ShouldBeTrue();
        second.Succeeded.ShouldBeFalse("a refused call was recorded as a success");
        second.Error.ShouldNotBeNull().ShouldContain("The tool call did not start.", Case.Sensitive);
        second.Usage.ShouldBeNull();
    }

    [Fact]
    public async Task The_model_does_not_get_another_turn_after_the_refusal()
    {
        var tools = new ToolBodies();
        var provider = Script();

        await using var host = await StartAsync(
            provider,
            tools.Speaks(First, amount: 5m),
            tools.Counts(Second),
            options => options.AgentGraph.MaxTotalCost = 1m);

        await RunUntilRefusedAsync(host);

        // Only the turn that asked for the tools reached the provider. The refusal sentence is
        // for the record and the log: the next model call stops at the budget as it always did.
        provider.Requests.Count.ShouldBe(1);
    }

    // --- What is not a boundary ---

    [Fact]
    public async Task Both_tools_run_while_the_budget_is_not_spent()
    {
        var tools = new ToolBodies();

        await using var host = await StartAsync(
            tools.Speaks(First, amount: 0.25m),
            tools.Speaks(Second, amount: 0.25m),
            options => options.AgentGraph.MaxTotalCost = 1m);

        var response = await RunAgentAsync(host);

        response.Text.ShouldContain("Done", Case.Sensitive);
        tools.Count(First).ShouldBe(1);
        tools.Count(Second).ShouldBe(1);

        var run = await RunRecordAsync(host);

        run.Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task A_tool_that_passes_the_ceiling_runs_to_its_end_and_its_amount_is_kept()
    {
        var tools = new ToolBodies();

        await using var host = await StartAsync(
            tools.Speaks(First, amount: 5m),
            tools.Counts(Second),
            options => options.AgentGraph.MaxTotalCost = 1m);

        await RunUntilRefusedAsync(host);

        // The amount is known only when the tool ends, so the call that crosses the ceiling is
        // the last one that starts (K-999: work that has begun is not stopped).
        var run = await RunRecordAsync(host);

        run.NonTokenCost.ShouldNotBeNull().Amount.ShouldBe(5m);
    }

    // --- The other tool sources: each reaches the gate through the one chain ---

    [Fact]
    public async Task An_mcp_tool_does_not_start_once_the_cost_budget_is_spent()
    {
        await using var server = await LookupMcpServer.StartAsync("from-mcp");
        var tools = new ToolBodies();

        await using var host = await StartMcpAsync(
            server,
            tools.Speaks(First, amount: 5m),
            options => options.AgentGraph.MaxTotalCost = 1m);

        var outcome = await RunOverHttpAsync(host, McpAgent, "mcp-refused");

        outcome.Status.ShouldBe("Failed", outcome.Stream);
        tools.Count(First).ShouldBe(1);
        server.Calls.ShouldBe(0, "the refused MCP tool reached its server");
    }

    [Fact]
    public async Task An_mcp_tool_starts_while_the_budget_is_not_spent()
    {
        // The control: the same host, tools and turn, with an amount under the ceiling. Without
        // it the test above also passes when the MCP tool cannot be called at all.
        await using var server = await LookupMcpServer.StartAsync("from-mcp");
        var tools = new ToolBodies();

        await using var host = await StartMcpAsync(
            server,
            tools.Speaks(First, amount: 0.25m),
            options => options.AgentGraph.MaxTotalCost = 1m);

        var outcome = await RunOverHttpAsync(host, McpAgent, "mcp-allowed");

        outcome.Status.ShouldBe("Completed", outcome.Stream);
        server.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task A_workspace_tool_does_not_start_once_the_cost_budget_is_spent()
    {
        var tools = new ToolBodies();
        var store = new ReadCountingStore();

        await using var host = await StartWorkspaceAsync(
            tools.Speaks(First, amount: 5m),
            store,
            options => options.AgentGraph.MaxTotalCost = 1m);

        var runId = await SessionWorkspaceApprovalTests.QueueAsync(host, "session-refused");

        (await host.WaitForRunStatusAsync(runId, "Failed")).ShouldBe("Failed");
        tools.Count(First).ShouldBe(1);
        store.Reads.ShouldBe(0, "the refused workspace tool read the store");
    }

    [Fact]
    public async Task A_workspace_tool_starts_while_the_budget_is_not_spent()
    {
        var tools = new ToolBodies();
        var store = new ReadCountingStore();

        await using var host = await StartWorkspaceAsync(
            tools.Speaks(First, amount: 0.25m),
            store,
            options => options.AgentGraph.MaxTotalCost = 1m);

        var runId = await SessionWorkspaceApprovalTests.QueueAsync(host, "session-allowed");

        (await host.WaitForRunStatusAsync(runId, "Completed")).ShouldBe("Completed");
        store.Reads.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_knowledge_search_does_not_start_once_the_cost_budget_is_spent()
    {
        // Phase 246 (F-688): the knowledge search sends the query to the embedding provider, so a refusal that
        // comes after the embedding is a charge the tree had no budget for.
        var tools = new ToolBodies();
        var embeddings = new CountingEmbeddings();
        var store = new CountingKnowledgeStore();

        await using var host = await StartKnowledgeAsync(
            tools.Speaks(First, amount: 5m),
            embeddings,
            store,
            options => options.AgentGraph.MaxTotalCost = 1m);

        var outcome = await RunOverHttpAsync(host, KnowledgeAgent, "knowledge-refused");

        outcome.Status.ShouldBe("Failed", outcome.Stream);
        tools.Count(First).ShouldBe(1);
        embeddings.Calls.ShouldBe(0, "the refused search embedded its query");
        store.Searches.ShouldBe(0, "the refused search read the store");
    }

    [Fact]
    public async Task A_knowledge_search_starts_while_the_budget_is_not_spent()
    {
        var tools = new ToolBodies();
        var embeddings = new CountingEmbeddings();
        var store = new CountingKnowledgeStore();

        await using var host = await StartKnowledgeAsync(
            tools.Speaks(First, amount: 0.25m),
            embeddings,
            store,
            options => options.AgentGraph.MaxTotalCost = 1m);

        var outcome = await RunOverHttpAsync(host, KnowledgeAgent, "knowledge-allowed");

        outcome.Status.ShouldBe("Completed", outcome.Stream);
        embeddings.Calls.ShouldBe(1);
        store.Searches.ShouldBe(1);
    }

    [Fact]
    public async Task A_turn_with_a_tool_that_needs_approval_still_stops_at_the_approval_and_the_gate_does_not_touch_it()
    {
        // Measured: Microsoft Agent Framework's loop asks for approval BEFORE it invokes any
        // call of the turn, through the approval layer found by GetService, and the tools that
        // need no approval wait with it. The gate is on the invoking path, so it is not reached
        // and the approval flow is the one it was. The approved call runs in the run that
        // resumes it, which reads that run's budget.
        var tools = new ToolBodies();

        await using var host = await StartAsync(
            tools.Speaks(First, amount: 5m),
            tools.NeedsApproval(Second),
            options => options.AgentGraph.MaxTotalCost = 1m);

        var agent = await ResolveAgentAsync(host);
        var contents = new List<AIContent>();

        await foreach (var update in agent.RunStreamingAsync("go"))
        {
            contents.AddRange(update.Contents);
        }

        contents.OfType<ToolApprovalRequestContent>().ShouldHaveSingleItem();
        (await RunRecordAsync(host)).Status.ShouldBe(RunStatus.AwaitingApproval);
        tools.Count(First).ShouldBe(0);
        tools.Count(Second).ShouldBe(0);
    }

    // --- The one limit the gate cannot remove ---

    [Fact]
    public async Task Concurrent_tool_calls_all_start_before_any_of_them_reports_its_amount()
    {
        // 🚨 A known limit, pinned so that it is not mistaken for a hole. With
        // AllowConcurrentToolCalls the tools of one turn start together; the gate cannot stop a
        // sibling that has started, and no amount is known until a tool ends. An installation
        // that needs the ceiling to hold turns concurrent calls off.
        var tools = new ToolBodies();
        using var barrier = new Barrier(2);

        await using var host = await StartAsync(
            Script(),
            tools.Run(First, () => { barrier.SignalAndWait(TimeSpan.FromSeconds(10)); Spend(5m); }),
            tools.Run(Second, () => { barrier.SignalAndWait(TimeSpan.FromSeconds(10)); Spend(5m); }),
            options => options.AgentGraph.MaxTotalCost = 1m,
            allowConcurrentToolCalls: true);

        await RunUntilRefusedAsync(host);

        tools.Count(First).ShouldBe(1);
        tools.Count(Second).ShouldBe(1, "the sibling had started when the first amount arrived");
    }

    // --- Hosts ---

    private static FakeModelProvider Script()
        => new FakeModelProvider(Provider)
            .WithModel(new ModelDescriptor { Name = Model })
            .ForModel(Model, cfg => cfg
                .CallsTools((First, null), (Second, null))
                .EchoesLastToolResult("Done: ", inputTokens: 4, outputTokens: 6));

    private static Task<TraconTestHost> StartAsync(
        Func<ITraconBuilder, ITraconBuilder> first,
        Func<ITraconBuilder, ITraconBuilder> second,
        Action<TraconOptions> configureOptions,
        Action<IServiceCollection>? configureServices = null,
        bool allowConcurrentToolCalls = false)
        => StartAsync(Script(), first, second, configureOptions, configureServices, allowConcurrentToolCalls);

    private static Task<TraconTestHost> StartAsync(
        FakeModelProvider provider,
        Func<ITraconBuilder, ITraconBuilder> first,
        Func<ITraconBuilder, ITraconBuilder> second,
        Action<TraconOptions> configureOptions,
        Action<IServiceCollection>? configureServices = null,
        bool allowConcurrentToolCalls = false)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(provider);
                second(first(builder))
                    .AddAgent(new AgentDefinition
                    {
                        Name = Agent,
                        Description = "Narrates.",
                        Instructions = "Narrate.",
                        Model = new ModelBinding
                        {
                            Provider = Provider,
                            Model = Model,
                            ToolLoop = new ToolLoopSettings { AllowConcurrentToolCalls = allowConcurrentToolCalls },
                        },
                        ToolNames = [First, Second],
                        Origin = AgentDefinitionOrigin.Code,
                    });
            },
            configureServices: services =>
            {
                services.Configure<TraconOptions>(options =>
                {
                    options.Pricing.Currency = "USD";
                    configureOptions(options);
                });

                configureServices?.Invoke(services);
            });

    private const string McpServerName = "gate-srv";
    private const string McpTool = McpServerName + "_lookup";
    private const string McpAgent = "mcp-narrator";
    private const string WorkspaceTool = "file_access_read";
    private const string KnowledgeTool = "search_knowledge";
    private const string KnowledgeAgent = "knowledge-narrator";

    private static FakeModelProvider ScriptCalling(string secondTool, object? secondArguments)
        => new FakeModelProvider(Provider)
            .WithModel(new ModelDescriptor { Name = Model })
            .ForModel(Model, cfg => cfg
                .CallsTools((First, null), (secondTool, secondArguments))
                .EchoesLastToolResult("Done: ", inputTokens: 4, outputTokens: 6));

    /// <summary>A host with one real MCP server whose tool is the second call of the turn.</summary>
    private static async Task<TraconTestHost> StartMcpAsync(
        LookupMcpServer server,
        Func<ITraconBuilder, ITraconBuilder> first,
        Action<TraconOptions> configureOptions)
    {
        var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder.UseMcp();
                builder.AddModelProvider(ScriptCalling(McpTool, null));
                first(builder);
            },
            configureServices: services =>
            {
                services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true);
                services.Configure<TraconOptions>(options =>
                {
                    options.Pricing.Currency = "USD";
                    configureOptions(options);
                });
            });

        try
        {
            using (var saved = await host.Client.PutAsJsonAsync(
                       new Uri($"/tracon/api/mcp-servers/{McpServerName}", UriKind.Relative),
                       new { endpoint = server.Endpoint.ToString(), requiresApproval = false }))
            {
                saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
            }

            using (var refreshed = await host.Client.PostAsync(new Uri("/tracon/api/mcp-servers/refresh", UriKind.Relative), content: null))
            {
                refreshed.StatusCode.ShouldBe(HttpStatusCode.OK, await refreshed.Content.ReadAsStringAsync());
            }

            using var agent = await host.Client.PostAsJsonAsync(
                new Uri("/tracon/api/agents", UriKind.Relative),
                new AgentDefinitionRequest
                {
                    Name = McpAgent,
                    Instructions = "Narrate.",
                    Model = new ModelBinding { Provider = Provider, Model = Model },
                    ToolNames = [First, McpTool],
                });

            agent.StatusCode.ShouldBe(HttpStatusCode.Created, await agent.Content.ReadAsStringAsync());

            return host;
        }
        catch
        {
            await host.DisposeAsync();

            throw;
        }
    }

    /// <summary>A host with a code agent that has a session workspace; its read is the second call of the turn.</summary>
    private static Task<TraconTestHost> StartWorkspaceAsync(
        Func<ITraconBuilder, ITraconBuilder> first,
        AgentFileStore store,
        Action<TraconOptions> configureOptions)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(ScriptCalling(WorkspaceTool, new { fileName = "notes.md" }));
                first(builder)
                    .AddAgent(new AgentDefinition
                    {
                        Name = "drafter",
                        Instructions = "Work with files.",
                        Model = new ModelBinding { Provider = Provider, Model = Model },
                        Workspace = new WorkspaceSettings { Enabled = true, DisableReadApproval = true },
                        ToolNames = [First],
                        Origin = AgentDefinitionOrigin.Code,
                    });
            },
            configureServices: services =>
            {
                services.UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(20));
                services.AddSingleton(store);
                services.Configure<TraconOptions>(options =>
                {
                    options.Pricing.Currency = "USD";
                    configureOptions(options);
                });
            });

    /// <summary>A host with a code agent that searches the knowledge base; its search is the second call of the turn.</summary>
    private static Task<TraconTestHost> StartKnowledgeAsync(
        Func<ITraconBuilder, ITraconBuilder> first,
        CountingEmbeddings embeddings,
        CountingKnowledgeStore store,
        Action<TraconOptions> configureOptions)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(ScriptCalling(KnowledgeTool, new { query = "refund policy" }));
                first(builder)
                    .AddAgent(new AgentDefinition
                    {
                        Name = KnowledgeAgent,
                        Instructions = "Answer from the knowledge base.",
                        Model = new ModelBinding { Provider = Provider, Model = Model },
                        Memory = new MemorySettings { EnableVectorSearch = true, VectorCollection = "docs" },
                        ToolNames = [First],
                        Origin = AgentDefinitionOrigin.Code,
                    });
            },
            configureServices: services =>
            {
                services.Configure<TraconKnowledgeOptions>(static options => options.Dimensions = 3);
                services.AddSingleton<IVectorSearchStore>(store);
                services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(embeddings);
                services.Configure<TraconOptions>(options =>
                {
                    options.Pricing.Currency = "USD";
                    configureOptions(options);
                });
            });

    /// <summary>Runs a stored agent over HTTP and reads the run's status.</summary>
    private static async Task<(string Status, string Stream)> RunOverHttpAsync(TraconTestHost host, string agent, string session)
    {
        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/agents/{agent}/run", UriKind.Relative),
            new AgentRunRequest { Message = "go", SessionId = session });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var stream = await response.Content.ReadAsStringAsync();

        using var runs = await host.Client.GetAsync(new Uri($"/tracon/api/runs?sessionId={session}", UriKind.Relative));
        var status = (await TraconTestHost.ReadJsonAsync(runs)).EnumerateArray().ShouldHaveSingleItem()
            .GetProperty("status").GetString().ShouldNotBeNull();

        return (status, stream);
    }

    private static async Task<AIAgent> ResolveAgentAsync(TraconTestHost host)
        => (await host.Services.GetRequiredService<IAgentCatalog>()
            .ResolveAsync(Agent, culture: null, CancellationToken.None)).ShouldNotBeNull();

    private static async Task<AgentResponse> RunAgentAsync(TraconTestHost host)
        => await (await ResolveAgentAsync(host)).RunAsync("go");

    /// <summary>
    /// Runs the agent and expects the model call after the tools to be refused. The run streams,
    /// as the HTTP run does: a non-streaming run that fails reads no tool result, so it records
    /// no call row to look at.
    /// </summary>
    private static async Task RunUntilRefusedAsync(TraconTestHost host)
    {
        var agent = await ResolveAgentAsync(host);

        await Should.ThrowAsync<TraconRunBudgetExceededException>(async () =>
        {
            await foreach (var update in agent.RunStreamingAsync("go"))
            {
                _ = update;
            }
        });
    }

    private static async Task<RunRecord> RunRecordAsync(TraconTestHost host)
        => (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery()))
            .Single(static candidate => string.Equals(candidate.AgentName, Agent, StringComparison.Ordinal));

    private static async Task<ToolInvocationRecord> RefusedCallAsync(TraconTestHost host)
    {
        var run = await RunRecordAsync(host);

        return (await host.Services.GetRequiredService<IRunStore>().ListToolInvocationsAsync(run.Id))
            .Single(static call => string.Equals(call.ToolName, Second, StringComparison.Ordinal));
    }

    private static void Spend(decimal amount)
        => TraconToolUsage.Report(new ToolCallUsage
        {
            Unit = ToolUsageUnits.Characters,
            Quantity = 100m,
            Cost = amount,
            Currency = "USD",
        }).ShouldBeTrue();

    /// <summary>A file store that counts its reads and holds nothing.</summary>
    private sealed class ReadCountingStore : AgentFileStore
    {
        private int _reads;

        public int Reads => Volatile.Read(ref _reads);

        public override Task WriteAsync(string path, string content, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public override Task<string?> ReadAsync(string path, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _reads);

            return Task.FromResult<string?>(null);
        }

        public override Task<bool> DeleteAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public override Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public override Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public override Task<IReadOnlyList<FileStoreEntry>> ListChildrenAsync(string directory, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<FileStoreEntry>>([]);

        public override Task<IReadOnlyList<FileSearchResult>> SearchAsync(
            string directory,
            string regexPattern,
            string? globPattern = null,
            bool recursive = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<FileSearchResult>>([]);
    }

    /// <summary>The tool bodies of one test, and how often each was called.</summary>
    private sealed class ToolBodies
    {
        private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

        public int Count(string tool)
        {
            lock (_counts)
            {
                return _counts.GetValueOrDefault(tool);
            }
        }

        /// <summary>A tool that reports an amount, as a speech tool does.</summary>
        public Func<ITraconBuilder, ITraconBuilder> Speaks(string tool, decimal amount)
            => Run(tool, () => Spend(amount));

        /// <summary>A tool that does nothing but count its calls.</summary>
        public Func<ITraconBuilder, ITraconBuilder> Counts(string tool)
            => Run(tool, static () => { });

        /// <summary>A tool that waits for a person's approval before it runs.</summary>
        public Func<ITraconBuilder, ITraconBuilder> NeedsApproval(string tool)
            => builder => builder.AddTool(
                (Func<Task<string>>)(() => Task.Run(() =>
                {
                    lock (_counts)
                    {
                        _counts[tool] = _counts.GetValueOrDefault(tool) + 1;
                    }

                    return "spoken";
                })),
                name: tool,
                configure: static options => options.RequiresApproval = true);

        /// <summary>A tool that counts its calls and then runs <paramref name="body"/>.</summary>
        public Func<ITraconBuilder, ITraconBuilder> Run(string tool, Action body)
            => builder => builder.AddTool(
                (Func<Task<string>>)(() => Task.Run(() =>
                {
                    lock (_counts)
                    {
                        _counts[tool] = _counts.GetValueOrDefault(tool) + 1;
                    }

                    body();

                    return "spoken";
                })),
                name: tool);
    }
}
