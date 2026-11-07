using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tracon.Tests.Workflows;

/// <summary>The graphs the cancellation sweep runs.</summary>
public enum SweepGraph
{
    /// <summary>Two agents in a chain.</summary>
    Sequential,

    /// <summary>An agent, a function node and an agent, in a chain.</summary>
    FunctionNode,

    /// <summary>Two agents of a handoff pattern; the first hands the conversation to the second.</summary>
    Handoff,

    /// <summary>A code workflow that publishes two human requests and ends waiting for input.</summary>
    AwaitingInput,

    /// <summary>A chain whose first agent asks for approval of a tool call and ends waiting for the answer.</summary>
    ToolApproval,
}

/// <summary>How the swept run starts.</summary>
public enum SweepStart
{
    /// <summary>A new run.</summary>
    Fresh,

    /// <summary>The continuation of a run that waits for an answer, after the answer came in.</summary>
    Resume,
}

/// <summary>The tenant the stores of a sweep read; the sweep gives each of its runs a tenant of its own.</summary>
internal sealed class SweepTenantContext : ITenantContext
{
    private volatile string _tenantId = "sweep";

    /// <summary>Gets or sets the tenant every call reads.</summary>
    public string TenantId
    {
        get => _tenantId;
        set => _tenantId = value;
    }
}

/// <summary>The stores a workflow run writes to, as a backend hands them out.</summary>
/// <param name="Runs">The run store.</param>
/// <param name="Checkpoints">The checkpoint store.</param>
/// <param name="Requests">The inbox of human requests.</param>
/// <param name="Responses">The ledger of answers.</param>
internal sealed record SweepStores(
    IRunStore Runs,
    IWorkflowCheckpointStore Checkpoints,
    IWorkflowRequestStore Requests,
    IWorkflowResponseStore Responses);

/// <summary>
/// One run's worth of setup: the agents, the definition or the code workflow of a graph, the catalog that serves
/// them, and the runner that runs them over the stores of a backend.
/// </summary>
internal sealed class SweepWorld
{
    private const string ChainName = "chain";
    private const string FunctionName = "tag";

    private readonly WorkflowCatalog _catalog;
    private readonly WorkflowAgentCache _agentCache;
    private readonly SweepStores _stores;
    private readonly ITenantContext _tenant;

    private SweepWorld(
        WorkflowCatalog catalog,
        WorkflowAgentCache agentCache,
        SweepStores stores,
        ITenantContext tenant,
        string workflowName,
        SweepGraph graph)
    {
        _catalog = catalog;
        _agentCache = agentCache;
        _stores = stores;
        _tenant = tenant;
        WorkflowName = workflowName;
        Graph = graph;
    }

    /// <summary>Gets the graph the world runs.</summary>
    public SweepGraph Graph { get; }

    /// <summary>Gets the name of the workflow the world runs.</summary>
    public string WorkflowName { get; }

    /// <summary>Gets whether a run of the graph ends waiting for a human answer, so a resume has something to continue.</summary>
    public bool RestsWaiting => Graph is SweepGraph.AwaitingInput or SweepGraph.ToolApproval;

    /// <summary>Gets the request that starts a run.</summary>
    public WorkflowRunRequest RunRequest => new() { WorkflowName = WorkflowName, Message = "hello" };

    /// <summary>Builds the world of a graph over the stores of a backend.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="plan">The plan that counts the awaited calls of the agents and the function.</param>
    /// <param name="stores">The stores the agents record their runs in.</param>
    /// <param name="tenant">The tenant of the run.</param>
    /// <returns>The world.</returns>
    public static async Task<SweepWorld> CreateAsync(SweepGraph graph, FaultPlan plan, SweepStores stores, ITenantContext tenant)
    {
        var agents = new Dictionary<string, AIAgent>(StringComparer.Ordinal);
        var functions = new List<WorkflowFunctionRegistration>();
        var codeWorkflows = new List<CodeWorkflowRegistration>();
        WorkflowDefinition? definition = null;

        // The run store's own calls are counted; the rows the agents write (their wrapper writes them straight to
        // the store) are not, as in the sweep of the agent path.
        void AddAgent(string name, AIAgent agent)
            => agents[name] = TestRunRecording.Agent(agent, stores.Runs, tenant, new TraconRunRecordingOptions(), NullLogger<RunRecordingAgent>.Instance);

        var workflowName = ChainName;

        switch (graph)
        {
            case SweepGraph.Sequential:
                AddAgent("first", new SweepAgent("first", plan));
                AddAgent("second", new SweepAgent("second", plan));
                definition = new WorkflowDefinition { Name = ChainName, Kind = WorkflowKind.Sequential, AgentNames = ["first", "second"] };

                break;

            case SweepGraph.FunctionNode:
                AddAgent("first", new SweepAgent("first", plan));
                AddAgent("second", new SweepAgent("second", plan));
                functions.Add(TagFunction(plan));
                definition = new WorkflowDefinition
                {
                    Name = ChainName,
                    Kind = WorkflowKind.Sequential,
                    Nodes =
                    [
                        new WorkflowNodeReference { Name = "first", Kind = WorkflowNodeKind.Agent },
                        new WorkflowNodeReference { Name = FunctionName, Kind = WorkflowNodeKind.Function },
                        new WorkflowNodeReference { Name = "second", Kind = WorkflowNodeKind.Agent },
                    ],
                };

                break;

            case SweepGraph.Handoff:
                AddAgent("first", new SweepAgent("first", plan) { Behavior = SweepAgentBehavior.HandsOff });
                AddAgent("second", new SweepAgent("second", plan));
                definition = new WorkflowDefinition
                {
                    Name = ChainName,
                    Kind = WorkflowKind.Handoff,
                    AgentNames = ["first", "second"],
                    HandoffInstructions = "Hand the conversation to the second agent.",

                    // The pattern runs autonomously and asks the agent that answers for more turns until it has used these
                    // up; one keeps the graph to a handoff and the turn after it.
                    MaxIterations = 1,
                };

                break;

            case SweepGraph.AwaitingInput:
                codeWorkflows.Add(TwoQuestionWorkflow.Registration());
                workflowName = TwoQuestionWorkflow.Name;

                break;

            case SweepGraph.ToolApproval:
                AddAgent("first", new SweepAgent("first", plan) { Behavior = SweepAgentBehavior.AsksApproval });
                AddAgent("second", new SweepAgent("second", plan));
                definition = new WorkflowDefinition { Name = ChainName, Kind = WorkflowKind.Sequential, AgentNames = ["first", "second"] };

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(graph), graph, "Unknown sweep graph.");
        }

        var resolver = new CallableAgentResolver(new AgentCatalog(agents));
        var agentCache = new WorkflowAgentCache(resolver, tenant, NullLoggerFactory.Instance, Options.Create(new TraconOptions()));
        var services = new ServiceCollection().BuildServiceProvider();
        var compiler = new WorkflowDefinitionCompiler(resolver, agentCache, new WorkflowFunctionRegistry(functions, services));
        var definitions = new InMemoryWorkflowDefinitionStore();

        if (definition is not null)
        {
            await definitions.SaveAsync(tenant.TenantId, definition).ConfigureAwait(false);
        }

        var catalog = new WorkflowCatalog(codeWorkflows, definitions, compiler, tenant, services);

        return new SweepWorld(catalog, agentCache, stores, tenant, workflowName, graph);
    }

    /// <summary>Gets the awaited calls a swept run of the graph makes; a sweep that does not see them has swept nothing.</summary>
    /// <param name="start">How the swept run starts.</param>
    /// <returns>The names of the calls.</returns>
    public IReadOnlyList<string> ExpectedCalls(SweepStart start)
    {
        // The run store, the checkpoint store and the sink are on the path of every run.
        var expected = new List<string> { "StartRunAsync", "AppendEventAsync", "OnEventAsync", "CreateAsync", "CompleteRunAsync" };

        if (Graph is not SweepGraph.AwaitingInput)
        {
            expected.Add("AgentTurnAsync");
        }

        if (Graph == SweepGraph.FunctionNode)
        {
            expected.Add("FunctionNodeAsync");
        }

        // A run that ends waiting writes its requests to the inbox; one that continues after an answer reads the
        // checkpoint and the request it answers, and marks the answer delivered.
        if (RestsWaiting && (start == SweepStart.Fresh || Graph == SweepGraph.AwaitingInput))
        {
            expected.Add("AddAsync");
        }

        if (start == SweepStart.Resume)
        {
            expected.AddRange(["ListByRunAsync", "ReadAsync", "MarkDeliveredAsync"]);
        }

        return expected;
    }

    /// <summary>Builds the runner of the world; every store call and event it makes goes through the plan.</summary>
    /// <param name="plan">The plan that counts, hooks and fails the calls.</param>
    /// <param name="clock">The runner's clock.</param>
    /// <param name="registry">The registry the runner lists its active runs in.</param>
    /// <returns>The runner.</returns>
    public WorkflowRunner CreateRunner(FaultPlan plan, TimeProvider clock, RunCancellationRegistry registry)
        => TestWorkflowRunner.Create(
            _catalog,
            FaultProxy<IRunStore>.Create(_stores.Runs, plan),
            FaultProxy<IWorkflowCheckpointStore>.Create(_stores.Checkpoints, plan),
            _tenant,
            Options.Create(new TraconWorkflowOptions { MaxConcurrentRuns = 1 }),
            Options.Create(new TraconOptions()),
            NullLogger<WorkflowRunner>.Instance,
            timeProvider: clock,
            cancellationRegistry: registry,
            sinks: [FaultProxy<IRunEventSink>.Create(new NullSink(), plan)],
            responseStore: FaultProxy<IWorkflowResponseStore>.Create(_stores.Responses, plan),
            requestStore: FaultProxy<IWorkflowRequestStore>.Create(_stores.Requests, plan),
            agentCache: _agentCache);

    private static WorkflowFunctionRegistration TagFunction(FaultPlan plan)
    {
        Func<List<ChatMessage>, IWorkflowContext, CancellationToken, ValueTask<List<ChatMessage>>> handler = async (messages, _, cancellationToken) =>
        {
            await plan.EnterAsync("FunctionNodeAsync", cancellationToken).ConfigureAwait(false);

            return messages;
        };

        return new WorkflowFunctionRegistration(
            new WorkflowFunctionDescriptor
            {
                Name = FunctionName,
                InputType = typeof(List<ChatMessage>),
                OutputType = typeof(List<ChatMessage>),
            },
            _ => (executorId, _, _) => new FunctionExecutor<List<ChatMessage>, List<ChatMessage>>(executorId, handler));
    }

    /// <summary>The catalog the compiler resolves the agents of a definition from.</summary>
    private sealed class AgentCatalog(IReadOnlyDictionary<string, AIAgent> agents) : IServiceProvider, IAgentCatalog
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(IAgentCatalog) ? this : null;

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<AgentDescriptor>)
            [
                .. agents.Keys.Select(static name => new AgentDescriptor
                {
                    Name = name,
                    Description = $"Plays the role of {name}.",
                    Origin = AgentDefinitionOrigin.Database,
                    SourceName = "database",
                }),
            ]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture, CancellationToken cancellationToken)
            => new(agents.GetValueOrDefault(agentName));

        public ValueTask<AIAgent?> ResolveAsync(string agentName, int? version, string? culture = null, CancellationToken cancellationToken = default)
            => new(agents.GetValueOrDefault(agentName));
    }
}

/// <summary>What a <see cref="SweepAgent"/> does when its turn comes.</summary>
internal enum SweepAgentBehavior
{
    /// <summary>Answers with the last message it received, tagged with its name.</summary>
    Echo,

    /// <summary>Calls the tool that hands the conversation to the agent after it.</summary>
    HandsOff,

    /// <summary>Asks for approval of a tool call, and finishes the call when the answer comes in.</summary>
    AsksApproval,
}

/// <summary>An agent that makes one awaited call the plan counts, then answers by its <see cref="Behavior"/>.</summary>
internal sealed class SweepAgent(string name, FaultPlan plan) : AIAgent
{
    private static JsonElement EmptyState { get; } = JsonDocument.Parse("{}").RootElement.Clone();

    /// <summary>Gets a signal the agent sets when its turn starts.</summary>
    public TaskCompletionSource? Entered { get; init; }

    /// <summary>Gets a task the turn waits for before it answers; the turn does not wait when it is <see langword="null"/>.</summary>
    public Task? Hold { get; init; }

    /// <summary>Gets what the agent does on its turn.</summary>
    public SweepAgentBehavior Behavior { get; init; }

    /// <inheritdoc />
    public override string Name => name;

    /// <inheritdoc />
    public override string Description => $"Plays the role of {name}.";

    /// <inheritdoc />
    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await TakeTurnAsync(cancellationToken).ConfigureAwait(false);

        var last = messages.LastOrDefault()?.Text ?? string.Empty;

        return new AgentResponse(new ChatMessage(ChatRole.Assistant, $"[{name}]{last}"));
    }

    /// <inheritdoc />
    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await TakeTurnAsync(cancellationToken).ConfigureAwait(false);

        var contents = messages.SelectMany(static message => message.Contents).ToList();

        if (Behavior == SweepAgentBehavior.AsksApproval)
        {
            var call = new FunctionCallContent(
                "call-1",
                "cancel_order",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["orderId"] = "ORD-7" });

            if (contents.OfType<ToolApprovalResponseContent>().Any())
            {
                yield return new AgentResponseUpdate(ChatRole.Assistant, [call]);
                yield return new AgentResponseUpdate(ChatRole.Tool, [new FunctionResultContent("call-1", "ORD-7 canceled.")]);
                yield return new AgentResponseUpdate(ChatRole.Assistant, [new TextContent("done")]);
            }
            else
            {
                yield return new AgentResponseUpdate(ChatRole.Assistant, [new ToolApprovalRequestContent("request-1", call)]);
            }

            yield break;
        }

        if (Behavior == SweepAgentBehavior.HandsOff)
        {
            yield return new AgentResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("handoff-1", "handoff_to_1", new Dictionary<string, object?>(StringComparer.Ordinal))]);

            yield break;
        }

        var last = messages.LastOrDefault()?.Text ?? string.Empty;

        yield return new AgentResponseUpdate(ChatRole.Assistant, [new TextContent($"[{name}]{last}")]);
    }

    /// <inheritdoc />
    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
        => new(new SweepSession());

    /// <inheritdoc />
    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
        => new(new SweepSession());

    /// <inheritdoc />
    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
        AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
        => new(EmptyState);

    private async Task TakeTurnAsync(CancellationToken cancellationToken)
    {
        await plan.EnterAsync("AgentTurnAsync", cancellationToken).ConfigureAwait(false);
        Entered?.TrySetResult();

        if (Hold is not null)
        {
            await Hold.ConfigureAwait(false);
        }

        await Task.Yield();
    }

    private sealed class SweepSession : AgentSession;
}
