using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.NodeUsageHost;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A function node does not start once the call tree's cost or time budget is spent.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 F-407: the tree budget refused a model call and a new agent step, and a function node
/// started anyway and did its paid work (speech, render, GPU). A chain of function nodes only
/// never stopped at all. The gate sits in the function wrapper and reads the budget from the
/// AMBIENT run scope, in the background task Microsoft Agent Framework runs the node on; only
/// the real host shows that the scope and the failure both survive that hop.
/// </para>
/// <para>
/// Token is out on purpose: it is on by default (200,000), and a function node spends none.
/// </para>
/// </remarks>
public sealed class FunctionNodeBudgetGateTests
{
    // --- The two boundaries the gate stops at ---

    [Fact]
    public async Task A_function_node_does_not_start_once_the_cost_budget_is_spent()
    {
        var synthesized = new Calls();

        await using var host = await StartAsync(
            builder => builder
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("speak", Speaks(5m))
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("synthesize", Counts(synthesized)),
            configureOptions: options => options.AgentGraph.MaxTotalCost = 1m);

        await SaveAsync(host, Workflow, Function("speak"), Function("synthesize"));

        var runId = await RunAsync(host);
        var run = await RecordAsync(host, runId);

        // The same identity a refused agent step ends with.
        run.Status.ShouldBe(RunStatus.Failed);

        var error = run.Error.ShouldNotBeNull();

        error.Type.ShouldBe(TraconRunBudgetExceededException.RunBudgetExceededErrorType);
        error.Class.ShouldBe(RunErrorClass.QuotaExceeded);
        error.Message.ShouldContain("Could not start function node 'synthesize'", Case.Sensitive);
        error.Message.ShouldContain("cost budget is exhausted", Case.Sensitive);
        error.Message.ShouldContain("Tracon:AgentGraph:MaxTotalCost", Case.Sensitive);

        synthesized.Count.ShouldBe(0, "the refused node's handler was called");

        // The node that crossed the ceiling ran to its end and its item is kept.
        var items = await UsageAsync(host, runId);

        items.ShouldHaveSingleItem().NodeName.ShouldBe("speak");
        run.NonTokenCost.ShouldNotBeNull().Amount.ShouldBe(5m);
    }

    [Fact]
    public async Task A_function_node_does_not_start_once_the_time_budget_has_passed()
    {
        var clock = new ManualTimeProvider();
        var synthesized = new Calls();

        await using var host = await StartAsync(
            builder => builder
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "render",
                    _ => (messages, _, _) =>
                    {
                        // The node takes ten seconds of a five-second budget.
                        clock.Advance(TimeSpan.FromSeconds(10));

                        return new ValueTask<List<ChatMessage>>(messages);
                    })
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("synthesize", Counts(synthesized)),
            services => services.AddSingleton<TimeProvider>(clock),
            options => options.AgentGraph.MaxDuration = TimeSpan.FromSeconds(5));

        await SaveAsync(host, Workflow, Function("render"), Function("synthesize"));

        var run = await RecordAsync(host, await RunAsync(host));

        run.Status.ShouldBe(RunStatus.Failed);

        var error = run.Error.ShouldNotBeNull();

        error.Type.ShouldBe(TraconRunBudgetExceededException.RunBudgetExceededErrorType);
        error.Class.ShouldBe(RunErrorClass.QuotaExceeded);
        error.Message.ShouldContain("Could not start function node 'synthesize'", Case.Sensitive);
        error.Message.ShouldContain("time budget is exhausted", Case.Sensitive);
        error.Message.ShouldContain("Tracon:AgentGraph:MaxDuration", Case.Sensitive);
        synthesized.Count.ShouldBe(0);
    }

    // --- What is not a boundary ---

    [Fact]
    public async Task A_spent_token_budget_does_not_stop_a_function_node()
    {
        var spoken = new Calls();

        // One token is spent by the agent step's own turn (4 + 6); the node spends none.
        await using var host = await StartAsync(
            builder => builder
                .AddModelProvider(PricedProvider())
                .AddAgent(Writer())
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("speak", Counts(spoken)),
            configureOptions: options => options.AgentGraph.MaxTotalTokens = 1);

        await SaveAsync(host, Workflow, Agent("writer"), Function("speak"));

        var run = await RecordAsync(host, await RunAsync(host));

        run.Status.ShouldBe(RunStatus.Completed);
        spoken.Count.ShouldBe(1);

        // The limit really was passed: the step spent 4 + 6 tokens against a budget of 1.
        run.TreeUsage.ShouldNotBeNull().TotalTokens.GetValueOrDefault().ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task A_host_without_a_cost_or_time_ceiling_never_refuses_a_node()
    {
        var spoken = new Calls();

        // Defaults: MaxTotalCost and MaxDuration are off. 500 is far past any ceiling a host
        // that set one would keep.
        await using var host = await StartAsync(
            builder => builder
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("speak", Speaks(500m))
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("synthesize", Counts(spoken)));

        await SaveAsync(host, Workflow, Function("speak"), Function("synthesize"));

        var run = await RecordAsync(host, await RunAsync(host));

        run.Status.ShouldBe(RunStatus.Completed);
        spoken.Count.ShouldBe(1);
    }

    // --- What the refusal leaves behind ---

    [Fact]
    public async Task The_refused_run_failed_webhook_carries_what_was_spent_before()
    {
        var publisher = new RecordingPublisher();

        await using var host = await StartAsync(
            builder => builder
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("speak", Speaks(5m))
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("synthesize", Counts(new Calls())),
            services => services.AddSingleton<IWebhookPublisher>(publisher),
            options => options.AgentGraph.MaxTotalCost = 1m);

        await SaveAsync(host, Workflow, Function("speak"), Function("synthesize"));

        await RunAsync(host);

        var failed = publisher.Failed().ShouldHaveSingleItem();

        failed.Cost.ShouldBe(5m);
        failed.NonTokenCost.ShouldBe(5m);
        failed.Currency.ShouldBe("USD");
    }

    [Fact]
    public async Task The_refused_runs_quota_entry_counts_the_first_nodes_amount()
    {
        await using var host = await StartAsync(
            builder => builder
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("speak", Speaks(5m))
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("synthesize", Counts(new Calls())),
            configureOptions: options => options.AgentGraph.MaxTotalCost = 1m);

        await SaveAsync(host, Workflow, Function("speak"), Function("synthesize"));

        await RunAsync(host);

        var counter = await DailyCounterAsync(host, Workflow);

        // The refusal does not lose the money the first node spent (K-990).
        counter.Cost.ShouldBe(5m);
        counter.Runs.ShouldBe(1);
    }

    [Fact]
    public async Task A_failed_item_write_still_stops_the_next_node()
    {
        var synthesized = new Calls();

        // The budget hears of the amount when the node reports it, before the item is
        // written (NonTokenUsageReporter): a store that cannot write must not leave the
        // ceiling unseen. The run's own row stops at a lost write (NodeUsageStoreFailureTests),
        // so the proof is the node that never ran and the stream's end, not the row's status.
        await using var host = await StartAsync(
            builder =>
            {
                builder.Services.ThrowFrom<IRunStore>(
                    nameof(IRunStore.RecordNonTokenUsageAsync),
                    static () => new IOException("the item cannot be written"));

                builder
                    .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("speak", Speaks(5m, requireRecorded: false))
                    .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("synthesize", Counts(synthesized));
            },
            configureOptions: options => options.AgentGraph.MaxTotalCost = 1m);

        await SaveAsync(host, Workflow, Function("speak"), Function("synthesize"));

        var stream = await StreamAsync(host);

        synthesized.Count.ShouldBe(0, "the next node started although the ceiling was passed");
        stream.ShouldContain("Could not start function node", Case.Sensitive);
    }

    [Fact]
    public async Task A_retried_node_does_not_start_its_next_attempt_once_the_budget_is_spent()
    {
        var attempts = new Calls();

        // The first attempt spends past the ceiling and fails with a transient error. The real
        // registration is under test: the gate has to sit INSIDE the retry, or the second
        // attempt (new paid work) starts without being asked.
        await using var host = await StartAsync(
            builder => builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "render",
                _ => async (messages, context, _) =>
                {
                    attempts.Hit();
                    await context.ReportUsageAsync(Speech(5m));

                    throw new TraconProviderUnavailableException("render-farm");
                },
                options => options.RetryPolicy = new WorkflowNodeRetryPolicy
                {
                    MaxAttempts = 3,
                    InitialDelay = TimeSpan.FromMilliseconds(1),
                }),
            configureOptions: options => options.AgentGraph.MaxTotalCost = 1m);

        await SaveAsync(host, Workflow, Function("render"));

        var run = await RecordAsync(host, await RunAsync(host));

        attempts.Count.ShouldBe(1, "a second attempt started after the ceiling was passed");
        run.Status.ShouldBe(RunStatus.Failed);

        // The refusal is the end, not the first attempt's transient fault.
        var error = run.Error.ShouldNotBeNull();

        error.Type.ShouldBe(TraconRunBudgetExceededException.RunBudgetExceededErrorType);
        error.Message.ShouldContain("Could not start function node 'render'", Case.Sensitive);
    }

    // --- Every registration family ---

    [Fact]
    public async Task A_function_with_settings_is_gated_too()
    {
        var synthesized = new Calls();

        await using var host = await StartAsync(
            builder => builder
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("speak", Speaks(5m))
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>, GateSettings>(
                    "synthesize",
                    _ => (messages, _, _, _) =>
                    {
                        synthesized.Hit();

                        return new ValueTask<List<ChatMessage>>(messages);
                    }),
            configureOptions: options => options.AgentGraph.MaxTotalCost = 1m);

        await SaveAsync(host, Workflow, Function("speak"), Function("synthesize"));

        var run = await RecordAsync(host, await RunAsync(host));

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull().Type.ShouldBe(TraconRunBudgetExceededException.RunBudgetExceededErrorType);
        synthesized.Count.ShouldBe(0);
    }

    [Fact]
    public async Task A_node_registered_to_start_when_the_budget_is_spent_still_runs()
    {
        var stored = new Calls();
        var synthesized = new Calls();

        // `store` spends nothing and saves what `speak` has just paid for: it is exempt. The
        // exemption is the node's alone: the node after it is refused as any other.
        await using var host = await StartAsync(
            builder => builder
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("speak", Speaks(5m))
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "store",
                    _ => async (messages, context, _) =>
                    {
                        stored.Hit();

                        // Its own spend still counts against the budget.
                        await context.ReportUsageAsync(Speech(1m));

                        return messages;
                    },
                    options => options.StartsWhenBudgetIsSpent = true)
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("synthesize", Counts(synthesized)),
            configureOptions: options => options.AgentGraph.MaxTotalCost = 1m);

        await SaveAsync(host, Workflow, Function("speak"), Function("store"), Function("synthesize"));

        var run = await RecordAsync(host, await RunAsync(host));

        stored.Count.ShouldBe(1, "the exempt node was refused");
        synthesized.Count.ShouldBe(0);
        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull().Message.ShouldContain("function node 'synthesize'", Case.Sensitive);
        run.NonTokenCost.ShouldNotBeNull().Amount.ShouldBe(6m);
    }

    [Fact]
    public async Task A_run_whose_last_node_is_exempt_completes_after_the_ceiling_was_passed()
    {
        var stored = new Calls();

        await using var host = await StartAsync(
            builder => builder
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("speak", Speaks(5m))
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "store",
                    Counts(stored),
                    options => options.StartsWhenBudgetIsSpent = true),
            configureOptions: options => options.AgentGraph.MaxTotalCost = 1m);

        await SaveAsync(host, Workflow, Function("speak"), Function("store"));

        var run = await RecordAsync(host, await RunAsync(host));

        run.Status.ShouldBe(RunStatus.Completed);
        stored.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_function_with_settings_can_start_when_the_budget_is_spent()
    {
        var stored = new Calls();

        // The settings family is a second registration path (`TSettings`): the exemption has to
        // reach the wrapper there too, or a consumer's exempt node is refused without a sound.
        await using var host = await StartAsync(
            builder => builder
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("speak", Speaks(5m))
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>, GateSettings>(
                    "store",
                    _ => (messages, _, _, _) =>
                    {
                        stored.Hit();

                        return new ValueTask<List<ChatMessage>>(messages);
                    },
                    options => options.StartsWhenBudgetIsSpent = true),
            configureOptions: options => options.AgentGraph.MaxTotalCost = 1m);

        await SaveAsync(host, Workflow, Function("speak"), Function("store"));

        var run = await RecordAsync(host, await RunAsync(host));

        run.Status.ShouldBe(RunStatus.Completed);
        stored.Count.ShouldBe(1);
    }

    // --- Cancellation ---

    [Fact]
    public async Task A_run_cancelled_after_its_budget_was_spent_ends_Canceled_not_refused()
    {
        // The deadline is behind the run once `render` returns, and `render` ignores its token on
        // purpose: the cancel arrives while it is still going, and the NEXT node's start would
        // see a cancelled run and a spent budget together. The caller's stop has to win.
        //
        // Measured: this pins the OUTCOME, not the gate's own order. With the gate's cancellation
        // check removed this test stays green: either the engine never starts the next node of a
        // cancelled run, or the runner files a cancelled run as Canceled whatever it threw (the
        // two are not told apart here). The order inside the gate is pinned by the unit case
        // `A_cancelled_call_throws_the_cancellation_before_the_budget_refusal`.
        var clock = new ManualTimeProvider();
        var rendering = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var synthesized = new Calls();

        await using var host = await StartAsync(
            builder => builder
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "render",
                    _ => async (messages, _, _) =>
                    {
                        clock.Advance(TimeSpan.FromSeconds(10));
                        rendering.TrySetResult();
                        await release.Task;

                        return messages;
                    })
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("synthesize", Counts(synthesized)),
            services => services.AddSingleton<TimeProvider>(clock),
            options => options.AgentGraph.MaxDuration = TimeSpan.FromSeconds(5));

        await SaveAsync(host, Workflow, Function("render"), Function("synthesize"));

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/workflows/{Workflow}/run")
        {
            Content = JsonContent.Create(new WorkflowRunHttpRequest { Message = "go" }),
        };

        using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        await using var stream = await response.Content.ReadAsStreamAsync();
        await using var frames = SseReader.ReadAsync(stream).GetAsyncEnumerator();

        (await frames.MoveNextAsync()).ShouldBeTrue();

        var runId = System.Text.Json.JsonDocument.Parse(frames.Current.Data).RootElement.GetProperty("runId").GetGuid();

        await rendering.Task.WaitAsync(TimeSpan.FromSeconds(10));

        using var cancelled = await host.Client.PostAsync($"/tracon/api/runs/{runId}/cancel", content: null);

        cancelled.StatusCode.ShouldBe(System.Net.HttpStatusCode.Accepted);

        release.TrySetResult();

        var run = await host.WaitForTerminalRunAsync(runId);

        run.Status.ShouldBe(RunStatus.Canceled);
        string.Equals(run.Error?.Type, TraconRunBudgetExceededException.RunBudgetExceededErrorType, StringComparison.Ordinal)
            .ShouldBeFalse("a cancelled run was refused for its budget");
        synthesized.Count.ShouldBe(0);
    }

    // --- What does not change ---

    [Fact]
    public async Task The_run_streams_the_refused_node_failure_and_no_later_node_runs()
    {
        var synthesized = new Calls();
        var stored = new Calls();

        await using var host = await StartAsync(
            builder => builder
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("speak", Speaks(5m))
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("synthesize", Counts(synthesized))
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("store", Counts(stored)),
            configureOptions: options => options.AgentGraph.MaxTotalCost = 1m);

        await SaveAsync(host, Workflow, Function("speak"), Function("synthesize"), Function("store"));

        var runId = await RunAsync(host);

        var events = new List<RunEvent>();

        await foreach (var runEvent in host.Services.GetRequiredService<IRunStore>().ReadEventsAsync(runId))
        {
            events.Add(runEvent);
        }

        // Measured (F-407): the engine announces the node before its handler runs, so a refused
        // node reads "invoked", then "failed" under its own name, as any node that throws does.
        // No later node is announced at all.
        var nodeEvents = events
            .Where(static runEvent => runEvent.Type is RunEventType.ExecutorInvoked or RunEventType.ExecutorFailed)
            .Select(static runEvent => (runEvent.Type, runEvent.Text))
            .ToList();

        nodeEvents.ShouldContain((RunEventType.ExecutorInvoked, "synthesize"));
        nodeEvents.ShouldContain((RunEventType.ExecutorFailed, "synthesize"));
        nodeEvents.IndexOf((RunEventType.ExecutorInvoked, "synthesize"))
            .ShouldBeLessThan(nodeEvents.IndexOf((RunEventType.ExecutorFailed, "synthesize")));
        nodeEvents.ShouldNotContain(static node => string.Equals(node.Text, "store", StringComparison.Ordinal));

        synthesized.Count.ShouldBe(0);
        stored.Count.ShouldBe(0);
    }

    /// <summary>Runs the workflow over HTTP and returns what the stream carried, to its end.</summary>
    private static async Task<string> StreamAsync(TraconTestHost host)
    {
        using var response = await host.Client.PostAsJsonAsync(
            $"/tracon/api/workflows/{Workflow}/run",
            new WorkflowRunHttpRequest { Message = "go" });

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);

        return await response.Content.ReadAsStringAsync();
    }

    private static Func<IServiceProvider, Func<List<ChatMessage>, Microsoft.Agents.AI.Workflows.IWorkflowContext, CancellationToken, ValueTask<List<ChatMessage>>>> Speaks(
        decimal amount,
        bool requireRecorded = true)
        => _ => async (messages, context, _) =>
        {
            var recorded = await context.ReportUsageAsync(Speech(amount));

            if (requireRecorded)
            {
                recorded.ShouldBeTrue();
            }

            return messages;
        };

    private static Func<IServiceProvider, Func<List<ChatMessage>, Microsoft.Agents.AI.Workflows.IWorkflowContext, CancellationToken, ValueTask<List<ChatMessage>>>> Counts(
        Calls calls)
        => _ => (messages, _, _) =>
        {
            calls.Hit();

            return new ValueTask<List<ChatMessage>>(messages);
        };

    /// <summary>The settings type of the settings family; it has no member a node must set.</summary>
    public sealed class GateSettings
    {
        /// <summary>Gets or sets a voice no test reads.</summary>
        public string? Voice { get; init; }
    }

    private sealed class Calls
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Hit() => Interlocked.Increment(ref _count);
    }

    private sealed class RecordingPublisher : IWebhookPublisher
    {
        private readonly List<(string EventType, WebhookEventPayload Payload)> _events = [];

        public IReadOnlyList<WebhookRunSummary> Failed()
        {
            lock (_events)
            {
                return
                [
                    .. _events
                        .Where(static entry => string.Equals(entry.EventType, WebhookEvents.RunFailed, StringComparison.Ordinal))
                        .Select(static entry => entry.Payload.Run.ShouldNotBeNull()),
                ];
            }
        }

        public ValueTask<int> PublishAsync(
            string tenantId,
            string eventType,
            WebhookEventPayload payload,
            CancellationToken cancellationToken = default)
        {
            lock (_events)
            {
                _events.Add((eventType, payload));
            }

            return new ValueTask<int>(1);
        }
    }
}
