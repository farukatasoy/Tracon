using System.Net;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.NodeUsageHost;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// <c>WorkflowFunctionOptions.MaxConcurrency</c> (phase 211): a function that is costly to run in parallel (a GPU voice
/// synthesis, a render) runs at most that many calls at once in the process, across runs and workflows.
/// </summary>
/// <remarks>
/// <para>
/// The only limit the engine had was <c>MaxConcurrentRuns</c>: run slots every tenant shares. The limit here belongs to
/// the function and is taken per attempt - inside the retry, so a node that waits to retry gives its slot back; outside
/// the budget gate, so the budget is read after the wait; outside the timeout, so the wait is not the node's own time.
/// The runs are real, started together against the real engine: only a real host shows that a waiting call keeps no
/// slot of its own when its run is cancelled.
/// </para>
/// </remarks>
public sealed class WorkflowFunctionConcurrencyTests
{
    private const string Flow = "media";

    [Fact]
    public async Task No_more_calls_run_at_once_than_the_function_allows_across_runs()
    {
        var node = new BlockingNode();

        await using var host = await StartAsync(node, maxConcurrency: 2);

        var runs = Enumerable.Range(0, 5).Select(index => RunAsync(host, Flow, $"run-{index}")).ToList();

        await WaitUntil.TrueAsync(() => node.Running == 2, "two calls to be running");

        // A third call has had every chance to start by now.
        await Task.Delay(300);
        node.Running.ShouldBe(2);

        node.Open();

        foreach (var runId in await Task.WhenAll(runs))
        {
            (await RecordAsync(host, runId)).Status.ShouldBe(RunStatus.Completed);
        }

        node.MostAtOnce.ShouldBe(2);
        node.Calls.ShouldBe(5);
    }

    [Fact]
    public async Task A_function_without_a_limit_runs_every_call_at_once()
    {
        var node = new BlockingNode();

        await using var host = await StartAsync(node, maxConcurrency: null);

        var runs = Enumerable.Range(0, 3).Select(index => RunAsync(host, Flow, $"run-{index}")).ToList();

        await WaitUntil.TrueAsync(() => node.Running == 3, "three calls to be running");

        node.Open();
        await Task.WhenAll(runs);
    }

    [Fact]
    public async Task A_node_that_waits_to_retry_does_not_hold_its_slot()
    {
        var order = new List<string>();

        await using var host = await NodeUsageHost.StartAsync(
            builder =>
            {
                builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "render",
                    _ => (messages, _, _) =>
                    {
                        var who = messages[0].Text;
                        var first = false;

                        lock (order)
                        {
                            order.Add(who);
                            first = order.Count(entry => string.Equals(entry, who, StringComparison.Ordinal)) == 1;
                        }

                        if (string.Equals(who, "A", StringComparison.Ordinal) && first)
                        {
                            throw new TraconProviderUnavailableException("render-farm");
                        }

                        return new ValueTask<List<ChatMessage>>(messages);
                    },
                    static options =>
                    {
                        options.MaxConcurrency = 1;
                        options.RetryPolicy = new WorkflowNodeRetryPolicy { MaxAttempts = 2, InitialDelay = TimeSpan.FromMilliseconds(600) };
                    });

                builder.AddWorkflow(Flow, Graph);
            });

        var a = RunAsync(host, Flow, "A");

        await WaitUntil.TrueAsync(() => { lock (order) { return order.Count == 1; } }, "A's first attempt");

        // A is in its retry delay now. With the slot held, B would wait for the whole delay and A's second attempt.
        var b = await RunAsync(host, Flow, "B");

        lock (order)
        {
            order.ShouldBe(["A", "B"], "B did not get the slot A left for its retry delay");
        }

        (await RecordAsync(host, b)).Status.ShouldBe(RunStatus.Completed);
        (await RecordAsync(host, await a)).Status.ShouldBe(RunStatus.Completed);

        lock (order)
        {
            order.ShouldBe(["A", "B", "A"]);
        }
    }

    [Fact]
    public async Task A_run_cancelled_while_it_waits_gives_up_the_wait_and_takes_no_slot()
    {
        var node = new BlockingNode();

        await using var host = await StartAsync(node, maxConcurrency: 1);

        var holder = RunAsync(host, Flow, "holder");

        await WaitUntil.TrueAsync(() => node.Running == 1, "the first call to hold the slot");

        using var accepted = await QueuedWorkflowHarness.PostAsync(
            host,
            $"/api/workflows/{Flow}/run",
            new WorkflowRunHttpRequest { Message = "waiter" },
            idempotencyKey: "waiter");
        var waiterId = await QueuedWorkflowHarness.AcceptedRunIdAsync(accepted);

        await WaitUntil.TrueAsync(
            () => host.Logs.AllText.Contains("waiting for a free slot", StringComparison.Ordinal),
            "the second call to be waiting for a slot");

        using var cancelled = await host.Client.PostAsync($"/tracon/api/runs/{waiterId}/cancel", content: null);

        cancelled.StatusCode.ShouldBe(HttpStatusCode.Accepted, await cancelled.Content.ReadAsStringAsync());
        await host.WaitForRunStatusAsync(waiterId, nameof(RunStatus.Canceled));

        // The cancelled call never ran, and the slot it queued for is still the holder's.
        node.Calls.ShouldBe(1);

        node.Open();
        (await RecordAsync(host, await holder)).Status.ShouldBe(RunStatus.Completed);

        // The slot was given back and nobody is left behind it: the next run starts and ends.
        var next = await RunAsync(host, Flow, "next");

        (await RecordAsync(host, next)).Status.ShouldBe(RunStatus.Completed);
        node.Calls.ShouldBe(2);
        node.MostAtOnce.ShouldBe(1);
    }

    [Fact]
    public async Task A_budget_that_runs_out_while_a_call_waits_refuses_it_when_it_gets_its_slot()
    {
        var clock = new ManualTimeProvider();
        var node = new BlockingNode();

        await using var host = await StartAsync(
            node,
            maxConcurrency: 1,
            configureServices: services => services.AddSingleton<TimeProvider>(clock),
            configureOptions: options => options.AgentGraph.MaxDuration = TimeSpan.FromSeconds(5));

        var holder = RunAsync(host, Flow, "holder");

        await WaitUntil.TrueAsync(() => node.Running == 1, "the first call to hold the slot");

        var waiter = RunAsync(host, Flow, "waiter");

        await WaitUntil.TrueAsync(
            () => host.Logs.AllText.Contains("waiting for a free slot", StringComparison.Ordinal),
            "the second call to be waiting for a slot");

        // Ten seconds go by against a five-second budget while the second call waits.
        clock.Advance(TimeSpan.FromSeconds(10));
        node.Open();

        (await RecordAsync(host, await holder)).Status.ShouldBe(RunStatus.Completed);

        var run = await RecordAsync(host, await waiter);

        run.Status.ShouldBe(RunStatus.Failed);

        var error = run.Error.ShouldNotBeNull();

        error.Type.ShouldBe(TraconRunBudgetExceededException.RunBudgetExceededErrorType);
        error.Message.ShouldContain("Could not start function node 'render'", Case.Sensitive);
        error.Message.ShouldContain("time budget is exhausted", Case.Sensitive);

        // Its handler never ran: the gate was read AFTER the wait.
        node.Calls.ShouldBe(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_limit_below_one_is_refused_when_the_function_is_registered(int limit)
    {
        var builder = new ServiceCollection().AddTracon();

        Should.Throw<ArgumentOutOfRangeException>(
                () => builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "render",
                    static _ => static (messages, _, _) => new ValueTask<List<ChatMessage>>(messages),
                    options => options.MaxConcurrency = limit))
            .Message.ShouldContain(nameof(WorkflowFunctionOptions.MaxConcurrency), Case.Sensitive);
    }

    private static Task<TraconTestHost> StartAsync(
        BlockingNode node,
        int? maxConcurrency,
        Action<IServiceCollection>? configureServices = null,
        Action<TraconOptions>? configureOptions = null)
        => NodeUsageHost.StartAsync(
            builder =>
            {
                builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "render",
                    _ => async (messages, _, token) =>
                    {
                        node.Enter();

                        try
                        {
                            await node.Gate.Task.WaitAsync(token);

                            return messages;
                        }
                        finally
                        {
                            node.Leave();
                        }
                    },
                    options => options.MaxConcurrency = maxConcurrency);

                builder.AddWorkflow(Flow, Graph);
            },
            services =>
            {
                services.Configure<TraconWorkflowOptions>(options => options.MaxConcurrentRuns = 10);
                services.Configure<TraconSchedulingOptions>(options => options.PollInterval = TimeSpan.FromMilliseconds(50));
                configureServices?.Invoke(services);
            },
            configureOptions);

    private static Workflow Graph(IServiceProvider services)
    {
        var render = services.GetWorkflowFunction("render");

        return new WorkflowBuilder(render).WithOutputFrom(render).WithName(Flow).Build();
    }

    private sealed class BlockingNode
    {
        private int _running;
        private int _most;
        private int _calls;

        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Running => Volatile.Read(ref _running);

        public int MostAtOnce => Volatile.Read(ref _most);

        public int Calls => Volatile.Read(ref _calls);

        public void Open() => Gate.TrySetResult();

        public void Enter()
        {
            Interlocked.Increment(ref _calls);

            var now = Interlocked.Increment(ref _running);
            int seen;

            while (now > (seen = Volatile.Read(ref _most)) && Interlocked.CompareExchange(ref _most, now, seen) != seen)
            {
            }
        }

        public void Leave() => Interlocked.Decrement(ref _running);
    }
}
