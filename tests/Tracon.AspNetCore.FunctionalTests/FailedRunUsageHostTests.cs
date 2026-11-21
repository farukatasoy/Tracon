using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A run that ends in an exception or a cancellation keeps the tokens the provider billed before it ended: on its row, in its
/// cost, and in the tokens the tenant's quota counts, through the real host and the real tool-call loop.
/// </summary>
/// <remarks>
/// <para>
/// The run recorder read a run's usage from its answer, and such a run has none. The row said it spent nothing, the quota
/// counted <c>runs: 1, tokens: 0</c>, and the cost was never calculated, although the provider had billed every call before the
/// failure. The model pipeline now counts each response as it arrives, and the recorder reads that count when a run ends
/// without an answer.
/// </para>
/// <para>
/// The scenarios cross the boundaries a unit test only imitates: the DI-built pipeline, the price catalog, the quota store, the
/// tree budget, and a child agent that fails inside its root's tree. Both the plain and the streaming path run, because the
/// run's scope lives in an <c>AsyncLocal</c> and the two paths set it differently.
/// </para>
/// </remarks>
public sealed class FailedRunUsageHostTests
{
    private const string Provider = "outage";
    private const string Model = "outage-model";
    private const string Agent = "outage-agent";
    private const string Tool = "lookup";

    // Each model call reports 40 input and 10 output tokens: 50 tokens, priced at 1000 and 2000 per million tokens.
    private const long InputPerCall = 40;
    private const long OutputPerCall = 10;
    private const decimal CostPerCall = 0.06m;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_run_that_fails_on_its_third_model_call_keeps_the_tokens_the_cost_and_the_quota_entry_of_the_two_calls_before(bool streaming)
    {
        var provider = new ScriptedProvider(failOnCall: 3);

        await using var host = await StartAsync(provider);

        await Should.ThrowAsync<Exception>(() => RunAsync(host, streaming));

        provider.CallCount().ShouldBe(3);

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Failed);

        var usage = run.Usage.ShouldNotBeNull();
        usage.InputTokens.ShouldBe(2 * InputPerCall);
        usage.OutputTokens.ShouldBe(2 * OutputPerCall);

        var spent = usage.TotalTokens.ShouldNotBeNull();
        spent.ShouldBe(2 * (InputPerCall + OutputPerCall));

        run.Cost.ShouldNotBeNull().Total().ShouldBe(2 * CostPerCall);

        // The quota counts the run once, with the tokens it spent and the money they cost.
        var counter = await DailyCounterAsync(host, Agent);

        counter.Runs.ShouldBe(1);
        counter.Tokens.ShouldBe(spent);
        counter.Cost.ShouldBe(2 * CostPerCall);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_run_the_budget_stops_keeps_the_tokens_the_budget_counted(bool streaming)
    {
        // Two calls spend 100 tokens against a cap of 90: the third is refused before it reaches the provider.
        var provider = new ScriptedProvider(failOnCall: int.MaxValue);

        await using var host = await StartAsync(provider, configureServices: static services =>
            services.Configure<TraconOptions>(static options => options.AgentGraph.MaxTotalTokens = 90));

        await Should.ThrowAsync<TraconRunBudgetExceededException>(() => RunAsync(host, streaming));

        provider.CallCount().ShouldBe(2);

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull().Type.ShouldBe(TraconRunBudgetExceededException.RunBudgetExceededErrorType);

        var spent = run.Usage.ShouldNotBeNull().TotalTokens.ShouldNotBeNull();
        spent.ShouldBe(2 * (InputPerCall + OutputPerCall));

        (await DailyCounterAsync(host, Agent)).Tokens.ShouldBe(spent);
    }

    [Fact]
    public async Task A_streamed_run_canceled_after_a_usage_part_counts_that_usage_in_the_quota()
    {
        var provider = new ScriptedProvider(failOnCall: int.MaxValue, streamUsageFirst: true, toolTurns: false);

        await using var host = await StartAsync(provider);

        await CancelAfterFirstUpdateAsync(host);

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Canceled);
        run.Usage.ShouldNotBeNull().TotalTokens.ShouldBe(InputPerCall + OutputPerCall);

        var counter = await DailyCounterAsync(host, Agent);

        counter.Runs.ShouldBe(1);
        counter.Tokens.ShouldBe(InputPerCall + OutputPerCall);
        counter.Cost.ShouldBe(CostPerCall);
    }

    [Fact]
    public async Task A_streamed_run_canceled_before_any_usage_part_arrived_records_no_usage_and_no_tokens()
    {
        var provider = new ScriptedProvider(failOnCall: int.MaxValue, streamUsageFirst: false, toolTurns: false);

        await using var host = await StartAsync(provider);

        await CancelAfterFirstUpdateAsync(host);

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Canceled);
        run.Usage.ShouldBeNull();

        // The run is counted; nothing was observed, so no tokens are.
        var counter = await DailyCounterAsync(host, Agent);

        counter.Runs.ShouldBe(1);
        counter.Tokens.ShouldBe(0);
    }

    [Fact]
    public async Task A_structured_response_rejected_on_every_attempt_counts_each_attempt_once()
    {
        // The validator folds the usage of every response it rejects, and the failed run reads what the pipeline
        // counted: the three attempts are in the run once, not twice.
        var provider = new FakeModelProvider(Provider)
            .WithModel(new ModelDescriptor { Name = Model, SupportsStructuredOutput = true })
            .RespondsWith("not valid json", inputTokens: 12, outputTokens: 3)
            .RespondsWith("still not valid json", inputTokens: 14, outputTokens: 4)
            .RespondsWith("never valid json", inputTokens: 16, outputTokens: 5);

        await using var host = await TraconTestHost.StartAsync(
            builder => builder.AddModelProvider(provider).AddAgent(AgentDefinitionFor(structured: true)),
            configureServices: static services => services.Configure<TraconStructuredResponseOptions>(static options =>
            {
                options.Enabled = true;
                options.MaxRepairAttempts = 2;
            }));

        await Should.ThrowAsync<TraconStructuredResponseException>(() => RunAsync(host, streaming: false));

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Failed);

        var usage = run.Usage.ShouldNotBeNull();
        usage.InputTokens.ShouldBe(12 + 14 + 16);
        usage.OutputTokens.ShouldBe(3 + 4 + 5);

        (await DailyCounterAsync(host, Agent)).Tokens.ShouldBe(usage.TotalTokens.ShouldNotBeNull());
    }

    [Fact]
    public async Task A_failed_child_writes_its_own_usage_on_its_own_row_and_the_root_does_not_count_it_twice()
    {
        // The root asks its child for work and the child's second model call fails after its first spent tokens.
        var child = new ScriptedProvider(failOnCall: 2, name: "child-provider", model: "child-model");

        await using var host = await StartTreeAsync(child);

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();
        var router = (await catalog.ResolveAsync("router", cancellationToken: CancellationToken.None)).ShouldNotBeNull();

        // The root waits for its child's result, so the child has ended and closed its row when the root answers.
        (await router.RunAsync("start")).Text.ShouldContain("Delegated", Case.Sensitive);

        var rows = await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery { OnlyRootRuns = false });

        var root = rows.Single(static row => row.Depth == 0);
        var childRow = rows.Single(static row => row.Depth == 1);

        child.CallCount().ShouldBe(2);

        // The child's row holds what the child spent before it failed.
        childRow.Status.ShouldBe(RunStatus.Failed);
        childRow.Usage.ShouldNotBeNull().TotalTokens.ShouldBe(InputPerCall + OutputPerCall);

        // The root's own row holds the root's own turn only: 4 + 6.
        root.Status.ShouldBe(RunStatus.Completed);
        root.Usage.ShouldNotBeNull().TotalTokens.ShouldBe(10);

        // The tree is the two rows added once: the root's 10 and the child's 50.
        root.TreeUsage.ShouldNotBeNull().TotalTokens.ShouldBe(10 + InputPerCall + OutputPerCall);

        // Every run writes its own consumption under the root's name.
        (await DailyCounterAsync(host, "router")).Tokens.ShouldBe(10 + InputPerCall + OutputPerCall);
    }

    // ---- hosts and runs ----

    private static Task<TraconTestHost> StartAsync(ScriptedProvider provider, Action<IServiceCollection>? configureServices = null)
        => TraconTestHost.StartAsync(
            builder => builder
                .AddModelProvider(provider)
                .AddTool((Func<Task<string>>)(() => Task.FromResult("found")), name: Tool)
                .AddAgent(AgentDefinitionFor(structured: false)),
            configureServices: configureServices);

    private static Task<TraconTestHost> StartTreeAsync(ScriptedProvider child)
        => TraconTestHost.StartAsync(builder =>
        {
            const string StartTask = "background_agents_start_task";
            const string WaitForCompletion = "background_agents_wait_for_first_completion";
            const string GetResults = "background_agents_get_task_results";

            var router = new FakeModelProvider("router-provider")
                .WithModel(new ModelDescriptor { Name = "router-model" })
                .ForModel("router-model", cfg => cfg
                    .CallsTool(StartTask, new { agentName = "worker", input = "sub task", description = "sub task" })
                    .CallsTool(WaitForCompletion, new { taskIds = new[] { 1 } })
                    .CallsTool(GetResults, new { taskId = 1 })
                    .EchoesLastToolResult("Delegated: ", inputTokens: 4, outputTokens: 6));

            builder
                .AddModelProvider(router)
                .AddModelProvider(child)
                .AddTool((Func<Task<string>>)(() => Task.FromResult("found")), name: Tool)
                .AddAgent(new AgentDefinition
                {
                    Name = "worker",
                    Description = "Does the work.",
                    Instructions = "Work.",
                    Model = new ModelBinding { Provider = child.Name, Model = child.ModelName },
                    ToolNames = [Tool],
                    Origin = AgentDefinitionOrigin.Code,
                })
                .AddAgent(new AgentDefinition
                {
                    Name = "router",
                    Description = "Delegates the work.",
                    Instructions = "Delegate.",
                    Model = new ModelBinding { Provider = "router-provider", Model = "router-model" },
                    CallableAgents = ["worker"],
                    Origin = AgentDefinitionOrigin.Code,
                });
        });

    private static AgentDefinition AgentDefinitionFor(bool structured) => new()
    {
        Name = Agent,
        Description = "Fails on the call a test names.",
        Instructions = "Look things up.",
        ToolNames = structured ? [] : [Tool],
        Model = new ModelBinding
        {
            Provider = Provider,
            Model = Model,
            ResponseFormat = structured
                ? new AgentResponseFormat
                {
                    Kind = AgentResponseFormatKind.JsonSchema,
                    Schema = JsonDocument.Parse("{\"type\":\"object\"}").RootElement,
                    SchemaName = "answer",
                }
                : null,
        },
        Origin = AgentDefinitionOrigin.Code,
    };

    private static async Task RunAsync(TraconTestHost host, bool streaming)
    {
        var agent = await ResolveAsync(host);

        if (streaming)
        {
            await foreach (var update in agent.RunStreamingAsync("go", cancellationToken: TestContext.Current.CancellationToken))
            {
                _ = update;
            }

            return;
        }

        await agent.RunAsync("go", cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>Cancels the caller's token when the first update reaches it, and reads on until the stream ends.</summary>
    private static async Task CancelAfterFirstUpdateAsync(TraconTestHost host)
    {
        var agent = await ResolveAsync(host);

        using var cancellation = new CancellationTokenSource();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var update in agent.RunStreamingAsync("go", cancellationToken: cancellation.Token))
            {
                _ = update;

                await cancellation.CancelAsync();
            }
        });
    }

    private static async Task<AIAgent> ResolveAsync(TraconTestHost host)
        => (await host.Services.GetRequiredService<IAgentCatalog>().ResolveAsync(Agent, cancellationToken: CancellationToken.None))
            .ShouldNotBeNull();

    private static async Task<RunRecord> SingleRunAsync(TraconTestHost host)
        => (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery { AgentName = Agent })).ShouldHaveSingleItem();

    private static async Task<QuotaUsageRecord> DailyCounterAsync(TraconTestHost host, string agentName)
        => (await host.Services.GetRequiredService<IQuotaStore>().GetUsageAsync(new QuotaUsageQuery
        {
            TenantId = host.Services.GetRequiredService<IOptions<TraconOptions>>().Value.DefaultTenantId,
            AgentName = agentName,
        })).Single(static record => record.Period == QuotaPeriod.Daily);

    // ---- doubles ----

    /// <summary>
    /// A provider whose every call asks for the tool and reports 40 input and 10 output tokens, and whose call number
    /// <c>failOnCall</c> fails with a provider outage, the way an upstream that goes down in the middle of a run does.
    /// </summary>
    /// <remarks>
    /// With <c>toolTurns</c> off a call streams a plain answer in three parts instead, with the usage in the first part or in
    /// the second, so a consumer that cancels after the first part has seen the usage or has not.
    /// </remarks>
    private sealed class ScriptedProvider : IModelProvider
    {
        private readonly int _failOnCall;
        private readonly bool _streamUsageFirst;
        private readonly bool _toolTurns;
        private int _calls;

        public ScriptedProvider(
            int failOnCall,
            string name = Provider,
            string model = Model,
            bool streamUsageFirst = false,
            bool toolTurns = true)
        {
            _failOnCall = failOnCall;
            _streamUsageFirst = streamUsageFirst;
            _toolTurns = toolTurns;
            Name = name;
            ModelName = model;
            Models =
            [
                new ModelDescriptor
                {
                    Name = model,
                    SupportsStructuredOutput = true,
                    InputCostPerMillionTokens = 1_000m,
                    OutputCostPerMillionTokens = 2_000m,
                },
            ];
        }

        public string Name { get; }

        public string ModelName { get; }

        /// <summary>Gets how many model calls reached the provider.</summary>
        public int CallCount() => Volatile.Read(ref _calls);

        public IReadOnlyList<ModelDescriptor> Models { get; }

        public IChatClient CreateChatClient(ModelBinding binding) => new Client(this);

        private static UsageDetails Usage()
            => new() { InputTokenCount = InputPerCall, OutputTokenCount = OutputPerCall, TotalTokenCount = InputPerCall + OutputPerCall };

        private sealed class Client(ScriptedProvider owner) : IChatClient
        {
            public Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
            {
                var call = Interlocked.Increment(ref owner._calls);

                return call == owner._failOnCall
                    ? throw new FakeUpstreamOutageException("The provider failed to resolve DNS.")
                    : Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [Call(call)])) { Usage = Usage() });
            }

            public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                var call = Interlocked.Increment(ref owner._calls);

                if (call == owner._failOnCall)
                {
                    throw new FakeUpstreamOutageException("The provider failed to resolve DNS.");
                }

                if (!owner._toolTurns)
                {
                    // Three parts, with a break between them where the consumer's cancellation is seen.
                    var usagePart = new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(Usage())]);
                    var textPart = new ChatResponseUpdate(ChatRole.Assistant, "part of an answer");

                    yield return owner._streamUsageFirst ? usagePart : textPart;

                    await Task.Yield();
                    cancellationToken.ThrowIfCancellationRequested();

                    yield return owner._streamUsageFirst ? textPart : usagePart;

                    await Task.Yield();
                    cancellationToken.ThrowIfCancellationRequested();

                    yield return new ChatResponseUpdate(ChatRole.Assistant, " and its end");

                    yield break;
                }

                await Task.Yield();

                yield return new ChatResponseUpdate(ChatRole.Assistant, [Call(call), new UsageContent(Usage())]);
            }

            public object? GetService(Type serviceType, object? serviceKey = null) => null;

            public void Dispose()
            {
                // The fake client has no resources to release.
            }

            private static FunctionCallContent Call(int call)
                => new($"call-{call}", Tool, arguments: null);
        }
    }
}
