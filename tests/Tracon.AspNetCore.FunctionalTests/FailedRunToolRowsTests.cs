using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A run that does not stream keeps the rows and the events of the tool calls it made, whether it ends or fails (F-689).
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The recorder read a tool call from the messages of the model's answer, and a run that does not stream
/// (a queued run, a continuation, a workflow agent step, the <c>/v1/*</c> endpoints) has those messages only when it
/// succeeds. A run that ended in an exception wrote no row and no event for the calls it had made, and a run that
/// succeeded wrote a row with no duration. The operator who needs the trail most, when the run failed, had none.
/// </para>
/// <para>
/// The calls are now written at the outermost point of the tool chain, in a ledger the run opens, and read once when
/// the run closes. Every scenario here runs through the real host and the real tool-call loop; a tool body is called
/// by Microsoft Agent Framework, never by the test.
/// </para>
/// </remarks>
public sealed class FailedRunToolRowsTests
{
    private const string Provider = "rows-provider";
    private const string Model = "rows-model";
    private const string Agent = "rows-agent";
    private const string First = "spend_first";
    private const string Second = "spend_second";

    // --- A run that fails keeps the calls it made ---

    [Fact]
    public async Task A_queued_run_that_ends_on_a_budget_refusal_lists_the_call_that_ran_and_the_call_that_was_refused()
    {
        var clock = new SteppedClock();
        var tools = new ToolBodies(clock);

        await using var host = await StartAsync(
            Script(),
            tools.Speaks(First, amount: 5m, takes: TimeSpan.FromMilliseconds(250)),
            tools.Counts(Second),
            options => options.AgentGraph.MaxTotalCost = 1m,
            clock);

        var runId = await QueueAsync(host);

        (await host.WaitForRunStatusAsync(runId, "Failed")).ShouldBe("Failed");

        var rows = await ToolRowsAsync(host, runId);

        rows.Count.ShouldBe(2, "a tool call is one row, however the run ended");

        var first = rows.Single(static row => string.Equals(row.ToolName, First, StringComparison.Ordinal));
        var second = rows.Single(static row => string.Equals(row.ToolName, Second, StringComparison.Ordinal));

        first.Succeeded.ShouldBeTrue();
        first.Duration.ShouldBe(TimeSpan.FromMilliseconds(250));
        second.Succeeded.ShouldBeFalse("a refused call was recorded as a success");
        second.Error.ShouldNotBeNull().ShouldContain("The tool call did not start.", Case.Sensitive);
        tools.Count(Second).ShouldBe(0, "the refused tool's body was called");

        // The run ends as it always did, with the identity of every budget refusal.
        var run = await RunAsync(host, runId);

        run.Error.ShouldNotBeNull().Type.ShouldBe(TraconRunBudgetExceededException.RunBudgetExceededErrorType);

        // The events hold the same two calls, once each.
        var events = ToolEvents(await EventsAsync(host, runId));

        events.Count(static e => e.Type == RunEventType.ToolInvoking).ShouldBe(2);
        events.Count(static e => e.Type == RunEventType.ToolInvoked).ShouldBe(1);
        events.Count(static e => e.Type == RunEventType.ToolFailed).ShouldBe(1);
    }

    [Fact]
    public async Task A_provider_that_fails_after_a_tool_turn_leaves_the_row_and_the_events_of_the_call_that_ran()
    {
        var clock = new SteppedClock();
        var tools = new ToolBodies(clock);

        await using var host = await StartAsync(
            new ToolThenOutageProvider(Provider, Model, First),
            tools.Speaks(First, amount: 0m, takes: TimeSpan.FromMilliseconds(40)),
            tools.Counts(Second),
            static _ => { },
            clock);

        var runId = await QueueAsync(host);

        (await host.WaitForRunStatusAsync(runId, "Failed")).ShouldBe("Failed");

        var row = (await ToolRowsAsync(host, runId)).ShouldHaveSingleItem();

        row.ToolName.ShouldBe(First);
        row.Succeeded.ShouldBeTrue();
        row.Duration.ShouldBe(TimeSpan.FromMilliseconds(40));
        row.Result.ShouldNotBeNull().ShouldContain("spoken", Case.Sensitive);

        var events = ToolEvents(await EventsAsync(host, runId));
        var invoking = events.Single(static e => e.Type == RunEventType.ToolInvoking);
        var invoked = events.Single(static e => e.Type == RunEventType.ToolInvoked);

        invoking.ToolCallId.ShouldNotBeNullOrEmpty();
        invoked.ToolCallId.ShouldBe(invoking.ToolCallId);
        invoked.Sequence.ShouldBeGreaterThan(invoking.Sequence);
        invoked.Timestamp.ShouldBeGreaterThanOrEqualTo(invoking.Timestamp);
    }

    [Fact]
    public async Task A_run_cancelled_while_a_tool_runs_leaves_the_start_of_the_call_and_a_row_that_says_it_never_ended()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(new FakeModelProvider(Provider)
                .WithModel(new ModelDescriptor { Name = Model })
                .ForModel(Model, cfg => cfg.CallsTool(First).EchoesLastToolResult("Done: ")))
            .AddTool(
                (Func<CancellationToken, Task<string>>)(async cancellationToken =>
                {
                    started.SetResult();
                    await Task.Delay(Timeout.Infinite, cancellationToken); // delay: simulated

                    return "never";
                }),
                name: First)
            .AddAgent(AgentDefinitionFor(First)));

        var agent = (await host.Services.GetRequiredService<IAgentCatalog>()
            .ResolveAsync(Agent, culture: null, CancellationToken.None)).ShouldNotBeNull();

        using var cancellation = new CancellationTokenSource();

        // The run does not stream, as a queued run does not: it has no messages to read the call from.
        var run = agent.RunAsync("go", cancellationToken: cancellation.Token);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await run);

        var record = (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        record.Status.ShouldBe(RunStatus.Canceled);

        var row = (await ToolRowsAsync(host, record.Id)).ShouldHaveSingleItem();

        row.ToolName.ShouldBe(First);
        row.Succeeded.ShouldBeFalse();
        row.Error.ShouldBe("The run ended before the tool result arrived.");
        row.Duration.ShouldNotBeNull();

        var events = ToolEvents(await EventsAsync(host, record.Id));

        events.ShouldHaveSingleItem().Type.ShouldBe(RunEventType.ToolInvoking);
    }

    [Fact]
    public async Task Calls_that_run_at_the_same_time_each_keep_their_own_row_and_their_own_denial_when_the_run_fails()
    {
        // Barrier(2): tool_b is denied before its body runs, so only tool_a and tool_c reach it.
        using var barrier = new Barrier(2);
        var provider = new ToolThenOutageProvider(Provider, Model, "tool_a", "tool_b", "tool_c");

        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder
                .AddModelProvider(provider)
                .AddTool(
                    (Func<Task<string>>)(() => Task.Run(() =>
                    {
                        barrier.SignalAndWait(TimeSpan.FromSeconds(10));
                        TraconToolUsage.Report(new ToolCallUsage { Unit = ToolUsageUnits.Characters, Quantity = 1 });

                        return "result_a";
                    })),
                    name: "tool_a")
                .AddTool((Func<string>)(static () => "result_b"), name: "tool_b")
                .AddTool(
                    (Func<Task<string>>)(() => Task.Run(() =>
                    {
                        barrier.SignalAndWait(TimeSpan.FromSeconds(10));
                        TraconToolUsage.Report(new ToolCallUsage { Unit = ToolUsageUnits.Characters, Quantity = 3 });

                        return "result_c";
                    })),
                    name: "tool_c")
                .AddAgent(AgentDefinitionFor(["tool_a", "tool_b", "tool_c"], allowConcurrentToolCalls: true));
                builder.UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(20));
            },
            configureServices: static services =>
            {
                services.AddSingleton<IToolAuthorizationHandler>(new DenyingHandler("tool_b"));
            });

        var runId = await QueueAsync(host);

        (await host.WaitForRunStatusAsync(runId, "Failed")).ShouldBe("Failed");

        var rows = await ToolRowsAsync(host, runId);

        rows.Count.ShouldBe(3);

        var a = rows.Single(static row => string.Equals(row.ToolName, "tool_a", StringComparison.Ordinal));
        var b = rows.Single(static row => string.Equals(row.ToolName, "tool_b", StringComparison.Ordinal));
        var c = rows.Single(static row => string.Equals(row.ToolName, "tool_c", StringComparison.Ordinal));

        // Each call carries its own result, its own measurement and its own denial, not a sibling's.
        a.Result.ShouldNotBeNull().ShouldContain("result_a", Case.Sensitive);
        a.AuthorizationDenied.ShouldBeFalse();
        a.Usage.ShouldNotBeNull().Quantity.ShouldBe(1m);
        c.Result.ShouldNotBeNull().ShouldContain("result_c", Case.Sensitive);
        c.AuthorizationDenied.ShouldBeFalse();
        c.Usage.ShouldNotBeNull().Quantity.ShouldBe(3m);

        // A denial is an ordinary result, not a failure: the row says so by the flag, as it does on every other path.
        b.AuthorizationDenied.ShouldBeTrue();
        b.Succeeded.ShouldBeTrue();
        b.Result.ShouldNotBeNull().ShouldContain("Not authorized.", Case.Sensitive);

        // Six events, each call once, and the stream reads in the order the calls happened.
        var events = ToolEvents(await EventsAsync(host, runId));

        events.Count.ShouldBe(6);

        foreach (var row in rows)
        {
            var own = events.Where(e => string.Equals(e.ToolCallId, row.ToolCallId, StringComparison.Ordinal)).ToList();

            own.Count(static e => e.Type == RunEventType.ToolInvoking).ShouldBe(1);
            own.Count(static e => e.Type == RunEventType.ToolInvoked).ShouldBe(1);
            own.Single(static e => e.Type == RunEventType.ToolInvoked).Sequence
                .ShouldBeGreaterThan(own.Single(static e => e.Type == RunEventType.ToolInvoking).Sequence);
        }

        events.Select(static e => e.Timestamp).ShouldBe(events.Select(static e => e.Timestamp).Order());
    }

    // --- A run that succeeds keeps one row per call, with its duration ---

    [Fact]
    public async Task A_queued_run_that_succeeds_writes_one_row_and_one_pair_of_events_per_call_and_measures_the_duration()
    {
        var clock = new SteppedClock();
        var tools = new ToolBodies(clock);

        await using var host = await StartAsync(
            Script(),
            tools.Speaks(First, amount: 0m, takes: TimeSpan.FromMilliseconds(250)),
            tools.Speaks(Second, amount: 0m, takes: TimeSpan.FromMilliseconds(30)),
            static _ => { },
            clock);

        var runId = await QueueAsync(host);

        (await host.WaitForRunStatusAsync(runId, "Completed")).ShouldBe("Completed");

        var rows = await ToolRowsAsync(host, runId);

        rows.Count.ShouldBe(2, "the ledger and the messages know the same call; it is one row");

        var first = rows.Single(static row => string.Equals(row.ToolName, First, StringComparison.Ordinal));
        var second = rows.Single(static row => string.Equals(row.ToolName, Second, StringComparison.Ordinal));

        first.Duration.ShouldBe(TimeSpan.FromMilliseconds(250));
        second.Duration.ShouldBe(TimeSpan.FromMilliseconds(30));
        rows.ShouldAllBe(static row => row.Succeeded);

        // The row carries the moment the call settled, so the rows read in the order the calls ran.
        second.CreatedAt.ShouldBeGreaterThan(first.CreatedAt);

        var events = ToolEvents(await EventsAsync(host, runId));

        events.Count(static e => e.Type == RunEventType.ToolInvoking).ShouldBe(2);
        events.Count(static e => e.Type == RunEventType.ToolInvoked).ShouldBe(2);
    }

    [Fact]
    public async Task A_run_that_streams_still_writes_one_row_and_one_pair_of_events_per_call()
    {
        var clock = new SteppedClock();
        var tools = new ToolBodies(clock);

        await using var host = await StartAsync(
            Script(),
            tools.Speaks(First, amount: 0m, takes: TimeSpan.FromMilliseconds(250)),
            tools.Counts(Second),
            static _ => { },
            clock);

        // The HTTP run is the streaming one.
        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/agents/{Agent}/run", UriKind.Relative),
            new AgentRunRequest { Message = "go", SessionId = "rows-stream" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _ = await response.Content.ReadAsStringAsync();

        using var runs = await host.Client.GetAsync(new Uri("/tracon/api/runs?sessionId=rows-stream", UriKind.Relative));
        var runId = (await TraconTestHost.ReadJsonAsync(runs)).EnumerateArray().ShouldHaveSingleItem().GetProperty("id").GetGuid();

        var rows = await ToolRowsAsync(host, runId);

        rows.Count.ShouldBe(2);

        // The streaming path measures the pair of contents itself, as before.
        rows.Single(static row => string.Equals(row.ToolName, First, StringComparison.Ordinal)).Duration.ShouldBe(TimeSpan.FromMilliseconds(250));

        var events = ToolEvents(await EventsAsync(host, runId));

        events.Count(static e => e.Type == RunEventType.ToolInvoking).ShouldBe(2);
        events.Count(static e => e.Type == RunEventType.ToolInvoked).ShouldBe(2);
    }

    // --- Where the call belongs ---

    [Fact]
    public async Task A_call_made_in_a_child_run_belongs_to_the_child_run_and_not_to_its_parent()
    {
        var provider = new FakeModelProvider(Provider);

        provider.ForModel("router", router => router
            .CallsTool("research_agent", new { query = "sub task" })
            .EchoesLastToolResult("Delegated: "));
        provider.ForModel("researcher", researcher => researcher
            .CallsTool(First)
            .EchoesLastToolResult("Child saw: "));

        var tools = new ToolBodies();

        await using var host = await TraconTestHost.StartAsync(builder =>
        {
            builder.AddModelProvider(provider);
            tools.Counts(First)(builder);

            builder.AddAgent(new AgentDefinition
            {
                Name = "research-agent",
                Description = "Performs research.",
                Instructions = "Research.",
                Model = new ModelBinding { Provider = Provider, Model = "researcher" },
                ToolNames = [First],
                Origin = AgentDefinitionOrigin.Code,
            });

            builder.AddAgent(new AgentDefinition
            {
                Name = Agent,
                Description = "Delegates the work.",
                Instructions = "Delegate.",
                Model = new ModelBinding { Provider = Provider, Model = "router" },
                CallableAgents = ["research-agent"],
                SubAgents = new SubAgentSettings { CallMode = SubAgentCallMode.Tool },
                Origin = AgentDefinitionOrigin.Code,
            });
        });

        var agent = (await host.Services.GetRequiredService<IAgentCatalog>()
            .ResolveAsync(Agent, culture: null, CancellationToken.None)).ShouldNotBeNull();

        (await agent.RunAsync("start")).Text.ShouldContain("Delegated: Child saw:", Case.Sensitive);

        var runs = await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery { OnlyRootRuns = false });
        var root = runs.Single(static run => run.Depth == 0);
        var child = runs.Single(static run => run.Depth == 1);

        // The agent called as a tool is outside the chain, so the parent's row comes from its messages; the tool the
        // child called passed through the chain of the CHILD's run, and is the child's row alone.
        (await ToolRowsAsync(host, root.Id)).ShouldHaveSingleItem().ToolName.ShouldBe("research_agent");
        (await ToolRowsAsync(host, child.Id)).ShouldHaveSingleItem().ToolName.ShouldBe(First);
    }

    [Fact]
    public async Task The_rows_of_a_failed_run_belong_to_its_tenant()
    {
        var tools = new ToolBodies();

        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder.UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                });
                builder.AddModelProvider(new ToolThenOutageProvider(Provider, Model, First));
                tools.Counts(First)(builder).AddAgent(AgentDefinitionFor(First));
                builder.UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(20));
            });

        Guid runId;

        using (var queued = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{Agent}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "go", SessionId = "rows-tenant" }),
        })
        {
            queued.Headers.Add("Prefer", "respond-async");
            queued.Headers.Add("X-Tracon-Tenant", "acme");

            using var accepted = await host.Client.SendAsync(queued);
            accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());

            runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();
        }

        (await host.WaitForRunStatusAsync(runId, "Failed", tenant: "acme")).ShouldBe("Failed");

        using var own = await GetToolsAsync(host, runId, "acme");
        using var foreign = await GetToolsAsync(host, runId, "globex");

        own.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await own.Content.ReadFromJsonAsync<List<ToolInvocationRecord>>()).ShouldNotBeNull().ShouldHaveSingleItem().ToolName.ShouldBe(First);
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_workflow_agent_step_that_fails_leaves_the_rows_of_the_calls_it_made()
    {
        var tools = new ToolBodies();

        await using var host = await AgentStepHost.StartAsync(
            AgentStepHost.PricedProvider().CallsTool(First).EchoesLastToolResult(),
            AgentStepHost.WriterAgent(format: null, First),
            builder => tools.Speaks(First, amount: 5m)(builder),
            services => services.Configure<TraconOptions>(options =>
            {
                options.Pricing.Currency = "USD";
                options.AgentGraph.MaxTotalCost = 1m;
            }));

        var workflow = await AgentStepHost.RunAsync(host, AgentStepHost.Code);

        // The step's next model call is refused: the step fails, and so does the workflow.
        workflow.Status.ShouldBe(RunStatus.Failed);

        var step = (await AgentStepHost.StepRowsAsync(host, workflow.Id)).ShouldHaveSingleItem();

        step.Status.ShouldBe(RunStatus.Failed);

        var row = (await ToolRowsAsync(host, step.Id)).ShouldHaveSingleItem();

        row.ToolName.ShouldBe(First);
        row.Succeeded.ShouldBeTrue();
        row.Duration.ShouldNotBeNull();
        tools.Count(First).ShouldBe(1);
    }

    // --- Hosts ---

    private static FakeModelProvider Script()
        => new FakeModelProvider(Provider)
            .WithModel(new ModelDescriptor { Name = Model })
            .ForModel(Model, cfg => cfg
                .CallsTools((First, null), (Second, null))
                .EchoesLastToolResult("Done: ", inputTokens: 4, outputTokens: 6));

    private static AgentDefinition AgentDefinitionFor(string tool, bool allowConcurrentToolCalls = false)
        => AgentDefinitionFor([tool], allowConcurrentToolCalls);

    private static AgentDefinition AgentDefinitionFor(string[] tools, bool allowConcurrentToolCalls = false)
        => new()
        {
            Name = Agent,
            Description = "Spends.",
            Instructions = "Spend.",
            Model = new ModelBinding
            {
                Provider = Provider,
                Model = Model,
                ToolLoop = new ToolLoopSettings { AllowConcurrentToolCalls = allowConcurrentToolCalls },
            },
            ToolNames = tools,
            Origin = AgentDefinitionOrigin.Code,
        };

    private static Task<TraconTestHost> StartAsync(
        IModelProvider provider,
        Func<ITraconBuilder, ITraconBuilder> first,
        Func<ITraconBuilder, ITraconBuilder> second,
        Action<TraconOptions> configureOptions,
        TimeProvider? clock = null)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(provider);
                second(first(builder)).AddAgent(AgentDefinitionFor([First, Second]));
                builder.UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(20));
            },
            configureServices: services =>
            {
                services.Configure<TraconOptions>(options =>
                {
                    options.Pricing.Currency = "USD";
                    configureOptions(options);
                });

                if (clock is not null)
                {
                    services.AddSingleton(clock);
                }
            });

    // --- Reading ---

    private static async Task<Guid> QueueAsync(TraconTestHost host, string session = "rows-session")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{Agent}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "go", SessionId = session }),
        };
        request.Headers.Add("Prefer", "respond-async");

        using var accepted = await host.Client.SendAsync(request);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());

        return (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();
    }

    private static async Task<List<ToolInvocationRecord>> ToolRowsAsync(TraconTestHost host, Guid runId)
        => await host.Client.GetFromJsonAsync<List<ToolInvocationRecord>>(
               new Uri($"/tracon/api/runs/{runId}/tools", UriKind.Relative))
           ?? [];

    private static async Task<HttpResponseMessage> GetToolsAsync(TraconTestHost host, Guid runId, string tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/tracon/api/runs/{runId}/tools", UriKind.Relative));
        request.Headers.Add("X-Tracon-Tenant", tenant);

        return await host.Client.SendAsync(request);
    }

    private static async Task<RunRecord> RunAsync(TraconTestHost host, Guid runId)
        => (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId)).ShouldNotBeNull();

    private static async Task<List<RunEvent>> EventsAsync(TraconTestHost host, Guid runId)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in host.Services.GetRequiredService<IRunStore>().ReadEventsAsync(runId))
        {
            events.Add(runEvent);
        }

        return events;
    }

    private static List<RunEvent> ToolEvents(IEnumerable<RunEvent> events)
        => [.. events.Where(static e => e.Type is RunEventType.ToolInvoking or RunEventType.ToolInvoked or RunEventType.ToolFailed)];

    // --- Doubles ---

    /// <summary>
    /// A clock whose monotonic timestamp moves only when a tool body moves it, so a duration measured with it is exact.
    /// Its wall clock follows the real one (plus what was advanced) and its timers are the system's, because the job
    /// worker leases a job by the time the queue stamped it with and keeps polling by timer.
    /// </summary>
    private sealed class SteppedClock : TimeProvider
    {
        private long _advanced;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Read(ref _advanced);

        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + TimeSpan.FromTicks(Interlocked.Read(ref _advanced));

        public void Advance(TimeSpan amount) => Interlocked.Add(ref _advanced, amount.Ticks);
    }

    /// <summary>
    /// A provider whose first model call asks for every tool it was given in one turn and whose later calls fail with a
    /// provider outage, the way an upstream that goes down in the middle of a run does.
    /// </summary>
    private sealed class ToolThenOutageProvider(string name, string model, params string[] toolNames) : IModelProvider
    {
        public string Name { get; } = name;

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = model }];

        public IChatClient CreateChatClient(ModelBinding binding) => new Client(toolNames);

        private sealed class Client(string[] toolNames) : IChatClient
        {
            private int _calls;

            public Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
                => Interlocked.Increment(ref _calls) == 1
                    ? Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Calls())))
                    : throw new FakeUpstreamOutageException("The provider failed to resolve DNS.");

            public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                if (Interlocked.Increment(ref _calls) != 1)
                {
                    throw new FakeUpstreamOutageException("The provider failed to resolve DNS.");
                }

                await Task.Yield();

                yield return new ChatResponseUpdate(ChatRole.Assistant, Calls());
            }

            public object? GetService(Type serviceType, object? serviceKey = null) => null;

            public void Dispose()
            {
                // The fake client has no resources to release.
            }

            private List<AIContent> Calls()
                => [.. toolNames.Select(static name => (AIContent)new FunctionCallContent(Guid.NewGuid().ToString("N"), name, arguments: null))];
        }
    }

    private sealed class DenyingHandler(string deniedToolName) : IToolAuthorizationHandler
    {
        public ValueTask<ToolAuthorizationResult> AuthorizeAsync(
            ToolAuthorizationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(string.Equals(request.ToolName, deniedToolName, StringComparison.Ordinal)
                ? ToolAuthorizationResult.Deny("Not authorized.")
                : ToolAuthorizationResult.Allow());
    }

    /// <summary>The tool bodies of one test, and how often each was called.</summary>
    private sealed class ToolBodies(SteppedClock? clock = null)
    {
        private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

        public int Count(string tool)
        {
            lock (_counts)
            {
                return _counts.GetValueOrDefault(tool);
            }
        }

        /// <summary>A tool that takes <paramref name="takes"/> of the test clock and reports an amount, as a speech tool does.</summary>
        public Func<ITraconBuilder, ITraconBuilder> Speaks(string tool, decimal amount, TimeSpan takes = default)
            => Run(tool, () =>
            {
                clock?.Advance(takes);

                if (amount > 0m)
                {
                    TraconToolUsage.Report(new ToolCallUsage
                    {
                        Unit = ToolUsageUnits.Characters,
                        Quantity = 100m,
                        Cost = amount,
                        Currency = "USD",
                    }).ShouldBeTrue();
                }
            });

        /// <summary>A tool that does nothing but count its calls.</summary>
        public Func<ITraconBuilder, ITraconBuilder> Counts(string tool)
            => Run(tool, static () => { });

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
