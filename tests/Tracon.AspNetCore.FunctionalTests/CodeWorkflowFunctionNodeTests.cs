using System.Globalization;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.NodeUsageHost;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A function node of a graph built in code (phase 211, <c>GetWorkflowFunction</c>) has what the same function has in a
/// stored chain: its time limit, its retry policy, the budget gate, a cost line that names the node and a strict read of
/// its settings.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The wrapper those capabilities live in was reachable only from the compiler of a stored chain, and the budget gate
/// reads an <c>internal</c> member of the tree's budget: a consumer who wrote his own executor could copy the timeout and
/// the retry, but not the gate. The graph here is hand-built with <see cref="WorkflowBuilder"/> exactly as such a consumer
/// builds it, and the functions are registered with the ordinary <c>AddWorkflowFunction</c>.
/// </para>
/// </remarks>
public sealed class CodeWorkflowFunctionNodeTests
{
    private const string Flow = "code-nodes";

    // --- The time limit ---

    [Fact]
    public async Task A_registered_timeout_fails_the_run_of_a_code_graph()
    {
        await using var host = await StartAsync(
            builder => builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "render",
                static _ => async (_, _, token) =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);

                    return [];
                },
                static options => options.Timeout = TimeSpan.FromMilliseconds(200)),
            services => Chain(services, services.GetWorkflowFunction("render")));

        var run = await RecordAsync(host, await RunAsync(host, Flow));

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull().Type.ShouldBe(TraconWorkflowFunctionTimeoutException.WorkflowFunctionTimeoutErrorType);
    }

    [Fact]
    public async Task The_timeout_of_the_step_wins_over_the_registered_one()
    {
        await using var host = await StartAsync(
            builder => builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "render",
                static _ => async (_, _, token) =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);

                    return [];
                },
                static options => options.Timeout = TimeSpan.FromMinutes(10)),
            services => Chain(services, services.GetWorkflowFunction("render", static step => step.Timeout = TimeSpan.FromMilliseconds(200))));

        var run = await RecordAsync(host, await RunAsync(host, Flow));

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull().Type.ShouldBe(TraconWorkflowFunctionTimeoutException.WorkflowFunctionTimeoutErrorType);
    }

    // --- The retry policy ---

    [Fact]
    public async Task A_transient_failure_is_retried_and_the_run_completes()
    {
        var attempts = new int[1];

        await using var host = await StartAsync(
            builder => builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "render",
                _ => (messages, _, _) =>
                {
                    if (Interlocked.Increment(ref attempts[0]) < 3)
                    {
                        throw new TraconProviderUnavailableException("render-farm");
                    }

                    return new ValueTask<List<ChatMessage>>(messages);
                },
                static options => options.RetryPolicy = new WorkflowNodeRetryPolicy
                {
                    MaxAttempts = 3,
                    InitialDelay = TimeSpan.FromMilliseconds(1),
                }),
            services => Chain(services, services.GetWorkflowFunction("render")));

        var run = await RecordAsync(host, await RunAsync(host, Flow));

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);
        attempts[0].ShouldBe(3);
    }

    // --- The budget gate and the cost line ---

    [Fact]
    public async Task A_node_does_not_start_once_the_budget_is_spent_and_the_node_that_spent_it_names_itself()
    {
        var synthesized = new int[1];

        await using var host = await StartAsync(
            builder => builder
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "speak",
                    static _ => async (messages, context, _) =>
                    {
                        await context.ReportUsageAsync(Speech(5m));

                        return messages;
                    })
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "synthesize",
                    _ => (messages, _, _) =>
                    {
                        Interlocked.Increment(ref synthesized[0]);

                        return new ValueTask<List<ChatMessage>>(messages);
                    }),
            services => Chain(services, services.GetWorkflowFunction("speak"), services.GetWorkflowFunction("synthesize")),
            configureOptions: options => options.AgentGraph.MaxTotalCost = 1m);

        var runId = await RunAsync(host, Flow);
        var run = await RecordAsync(host, runId);

        // The same identity a refused node of a stored chain ends with.
        run.Status.ShouldBe(RunStatus.Failed);

        var error = run.Error.ShouldNotBeNull();

        error.Type.ShouldBe(TraconRunBudgetExceededException.RunBudgetExceededErrorType);
        error.Class.ShouldBe(RunErrorClass.QuotaExceeded);
        error.Message.ShouldContain("Could not start function node 'synthesize'", Case.Sensitive);
        synthesized[0].ShouldBe(0, "the refused node's handler was called");

        // The cost line carries the name the function was registered with.
        (await UsageAsync(host, runId)).ShouldHaveSingleItem().NodeName.ShouldBe("speak");
        run.NonTokenCost.ShouldNotBeNull().Amount.ShouldBe(5m);
    }

    [Fact]
    public async Task A_node_registered_to_start_when_the_budget_is_spent_still_runs_in_a_code_graph()
    {
        var stored = new int[1];

        await using var host = await StartAsync(
            builder => builder
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "speak",
                    static _ => async (messages, context, _) =>
                    {
                        await context.ReportUsageAsync(Speech(5m));

                        return messages;
                    })
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "store",
                    _ => (messages, _, _) =>
                    {
                        Interlocked.Increment(ref stored[0]);

                        return new ValueTask<List<ChatMessage>>(messages);
                    },
                    static options => options.StartsWhenBudgetIsSpent = true),
            services => Chain(services, services.GetWorkflowFunction("speak"), services.GetWorkflowFunction("store")),
            configureOptions: options => options.AgentGraph.MaxTotalCost = 1m);

        var run = await RecordAsync(host, await RunAsync(host, Flow));

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);
        stored[0].ShouldBe(1);
    }

    // --- The settings ---

    [Fact]
    public async Task The_handler_receives_the_settings_the_step_was_given_as_a_typed_value()
    {
        var seen = new List<string>();

        await using var host = await StartAsync(
            builder => builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>, VoiceSettings>(
                "tts",
                _ => (messages, settings, _, _) =>
                {
                    lock (seen)
                    {
                        seen.Add(string.Create(CultureInfo.InvariantCulture, $"{settings.Voice}@{settings.Speed}"));
                    }

                    return new ValueTask<List<ChatMessage>>(messages);
                }),
            services => Chain(services, services.GetWorkflowFunction("tts", static step => step.SetSettings(new VoiceSettings { Voice = "mira", Speed = 1.25 }))));

        var run = await RecordAsync(host, await RunAsync(host, Flow));

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);
        seen.ShouldBe(["mira@1.25"]);
    }

    [Fact]
    public async Task Settings_as_json_are_read_as_strictly_as_a_stored_nodes_and_a_wrong_field_stops_the_run_before_the_first_node()
    {
        var ran = new int[1];

        await using var host = await StartAsync(
            builder => builder
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "first",
                    _ => (messages, _, _) =>
                    {
                        Interlocked.Increment(ref ran[0]);

                        return new ValueTask<List<ChatMessage>>(messages);
                    })
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>, VoiceSettings>(
                    "tts",
                    static _ => static (messages, _, _, _) => new ValueTask<List<ChatMessage>>(messages)),
            services => Chain(
                services,
                services.GetWorkflowFunction("first"),
                services.GetWorkflowFunction("tts", static step => step.Settings = System.Text.Json.JsonDocument.Parse("""{"voice":"mira","speeed":2}""").RootElement.Clone())));

        var run = await RecordAsync(host, await RunAsync(host, Flow));

        run.Status.ShouldBe(RunStatus.Failed);
        ran[0].ShouldBe(0, "a node ran although the graph could not be built");

        var message = run.Error.ShouldNotBeNull().Message;

        message.ShouldContain($"Workflow '{Flow}' cannot run", Case.Sensitive);
        message.ShouldContain("Function node 'tts' has invalid settings", Case.Sensitive);
        message.ShouldContain("speeed", Case.Sensitive);
    }

    [Fact]
    public async Task Settings_given_to_a_function_that_declares_none_are_refused_by_name()
    {
        await using var host = await StartAsync(
            builder => builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "render",
                static _ => static (messages, _, _) => new ValueTask<List<ChatMessage>>(messages)),
            services => Chain(
                services,
                services.GetWorkflowFunction("render", static step => step.Settings = System.Text.Json.JsonDocument.Parse("""{"a":1}""").RootElement.Clone())));

        var run = await RecordAsync(host, await RunAsync(host, Flow));

        run.Status.ShouldBe(RunStatus.Failed);

        var message = run.Error.ShouldNotBeNull().Message;

        message.ShouldContain($"Workflow '{Flow}' cannot run", Case.Sensitive);
        message.ShouldContain("Function node 'render' carries settings, but the function declares none", Case.Sensitive);
    }

    [Fact]
    public async Task A_function_nobody_registered_fails_the_run_before_the_first_node_and_names_the_workflow_and_the_function()
    {
        await using var host = await StartAsync(
            builder => builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "render",
                static _ => static (messages, _, _) => new ValueTask<List<ChatMessage>>(messages)),
            services => Chain(services, services.GetWorkflowFunction("render"), services.GetWorkflowFunction("tts")));

        var run = await RecordAsync(host, await RunAsync(host, Flow));

        run.Status.ShouldBe(RunStatus.Failed);

        var message = run.Error.ShouldNotBeNull().Message;

        message.ShouldContain($"Workflow '{Flow}' cannot run", Case.Sensitive);
        message.ShouldContain("no workflow function registered with name 'tts'", Case.Sensitive);
    }

    [Fact]
    public async Task A_timeout_the_timer_cannot_hold_is_refused_when_the_graph_is_built()
    {
        await using var host = await StartAsync(
            builder => builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "render",
                static _ => static (messages, _, _) => new ValueTask<List<ChatMessage>>(messages)),
            services => Chain(services, services.GetWorkflowFunction("render")));

        Should.Throw<ArgumentOutOfRangeException>(
                () => host.Services.GetWorkflowFunction("render", static step => step.Timeout = TimeSpan.Zero))
            .Message.ShouldContain(nameof(WorkflowFunctionStepOptions.Timeout), Case.Sensitive);
    }

    private static Task<TraconTestHost> StartAsync(
        Action<ITraconBuilder> register,
        Func<IServiceProvider, Workflow> graph,
        Action<TraconOptions>? configureOptions = null)
        => NodeUsageHost.StartAsync(
            builder =>
            {
                register(builder);
                builder.AddWorkflow(Flow, graph);
            },
            configureOptions: configureOptions);

    private static Workflow Chain(IServiceProvider services, params ExecutorBinding[] nodes)
    {
        ArgumentNullException.ThrowIfNull(services);

        var builder = new WorkflowBuilder(nodes[0]).WithName(Flow);

        for (var index = 1; index < nodes.Length; index++)
        {
            builder = builder.AddEdge(nodes[index - 1], nodes[index]);
        }

        return builder.WithOutputFrom(nodes[^1]).Build();
    }

    private sealed record VoiceSettings
    {
        public required string Voice { get; init; }

        public double Speed { get; init; } = 1;
    }
}
