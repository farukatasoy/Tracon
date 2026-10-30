using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Tests.Common;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 266: the steps of a workflow are spans of the workflow's run.
/// </summary>
/// <remarks>
/// <para>
/// Before this phase the run trace of a workflow held the agents' own spans and nothing of the workflow: a slow
/// step read as a gap. A workflow Tracon builds from a stored definition is now built with the framework's
/// telemetry, writing to the Tracon source. A graph built in code is the consumer's: Tracon cannot add the ring to it,
/// so the run trace also listens to the framework's own source, and a graph that calls <c>WithOpenTelemetry()</c> shows
/// up while a graph that does not changes nothing.
/// </para>
/// <para>
/// 🚨 The framework runs the graph on a task it starts itself, so whether the span reaches the run depends on the
/// ambient <see cref="Activity"/> flowing into that task. A unit test of the builder would not notice; every scenario
/// here reads <c>GET /api/runs/{id}/trace</c>.
/// </para>
/// </remarks>
public sealed class WorkflowTraceTests
{
    private const string ExecutorSpan = "executor.process";
    private const string CodeFlow = "code-trace-flow";

    [Fact]
    public async Task A_stored_workflow_writes_its_executor_spans_into_the_run_trace()
    {
        await using var host = await StartStoredAsync();

        var trace = await RunStoredAsync(host);

        trace.Spans.Count(IsExecutorSpan).ShouldBeGreaterThanOrEqualTo(3, "one executor span per step");
        trace.Spans.ShouldContain(static span => string.Equals(span.Name, "workflow_invoke", StringComparison.Ordinal));

        // 🚨 The agents of the two agent steps are spans of the workflow's run too. The framework runs the graph on a
        // task it starts itself, and that task used to carry no ambient span: every agent and chat span of a workflow
        // was a root of its own trace, and the collector gave it to no run.
        RunTraceReader.Operations(trace, "invoke_agent").Count.ShouldBe(2);
        RunTraceReader.Operations(trace, "chat").Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_stored_workflow_keeps_executor_content_out_of_the_trace_even_when_sensitive_data_is_recorded()
    {
        // Open question 1, answer A: the framework option stays off. The store filter is a second line, not the first.
        await using var host = await StartStoredAsync(static options => options.Observability.RecordSensitiveData = true);

        var trace = await RunStoredAsync(host);

        trace.Spans.Count(IsExecutorSpan).ShouldBeGreaterThanOrEqualTo(3);
        trace.Spans.SelectMany(static span => span.Attributes.Keys)
            .ShouldNotContain(static key => key.StartsWith("executor.input", StringComparison.Ordinal)
                || key.StartsWith("executor.output", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_three_step_stored_workflow_stays_under_the_default_span_limit()
    {
        // The workflow spans share the run's MaxSpansPerRun with the chat spans. Message sends and edge groups are
        // off for a stored workflow so the budget goes to the spans a reader needs.
        await using var host = await StartStoredAsync();

        var trace = await RunStoredAsync(host);

        trace.Spans.Count.ShouldBeLessThan(
            new TraconObservabilityOptions().MaxSpansPerRun,
            $"the run kept {trace.Spans.Count} spans");
        host.Logs.Entries.ShouldNotContain(
            static entry => entry.Contains("span count exceeded", StringComparison.Ordinal),
            "the run hit MaxSpansPerRun and dropped spans");
        trace.Spans.ShouldNotContain(static span => string.Equals(span.Name, "message.send", StringComparison.Ordinal));
        trace.Spans.ShouldNotContain(static span => span.Name.StartsWith("edge_group.process", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_stored_pattern_writes_the_agents_spans_but_no_workflow_span()
    {
        // The five patterns (agentNames with Sequential, Concurrent, Handoff, GroupChat, Magentic) are built by the
        // framework's AgentWorkflowBuilder, which returns a built graph and has no place for the telemetry. The guide
        // says so; this pins it, so the day the framework offers a hook the claim is changed on purpose.
        await using var host = await StartStoredAsync();

        using var saved = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/workflows/pattern-flow", UriKind.Relative),
            new WorkflowSaveRequest { Kind = WorkflowKind.Sequential, AgentNames = ["writer", "editor"] },
            TestContext.Current.CancellationToken);

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var trace = await RunTraceReader.ReadAsync(host, await NodeUsageHost.RunAsync(host, "pattern-flow"));

        RunTraceReader.Operations(trace, "invoke_agent").Count.ShouldBe(2, "the agents are still spans of the run");
        trace.Spans.ShouldNotContain(static span => IsExecutorSpan(span) || span.Name.StartsWith("workflow", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_code_workflow_that_asks_for_telemetry_shows_its_executor_span_in_the_run_trace()
    {
        await using var host = await StartCodeAsync(telemetry: true);

        var trace = await RunCodeAsync(host);

        trace.Spans.ShouldContain(static span => IsExecutorSpan(span) && span.Name.EndsWith("trace-step", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_code_workflow_that_does_not_ask_for_telemetry_leaves_its_trace_as_it_was()
    {
        await using var host = await StartCodeAsync(telemetry: false);

        var trace = await RunCodeAsync(host);

        trace.Spans.ShouldNotContain(static span => IsExecutorSpan(span), "nothing was asked for, so nothing is written");
        trace.Spans.ShouldContain(static span => string.Equals(span.Name, TraconDiagnostics.RunActivityName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Two_workflows_that_share_a_trace_id_keep_their_own_spans()
    {
        // A gateway that propagates distributed tracing hands two runs one trace id. The framework's spans are
        // written from tasks the framework started, so a span that found its run by trace id alone would land in
        // whichever run completed first.
        //
        // The built-in stores key a trace by its trace id, so the two runs read back as one trace over HTTP. The
        // question here is which run the collector gave each span to, so the store is one that keeps each batch.
        var store = new RecordingTraceStore();

        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder.UseWorkflows();
                builder.AddWorkflow("flow-a", static _ => CodeGraph("flow-a", "alpha-step", telemetry: true));
                builder.AddWorkflow("flow-b", static _ => CodeGraph("flow-b", "beta-step", telemetry: true));
            },
            configureServices: services =>
            {
                services.AddSingleton<ITraceStore>(store);
                services.Configure<TraconOptions>(static options => options.Observability.SuccessSampleRatio = 1);
            });

        // The server span of a request is created only while something listens to ASP.NET Core's source, as a
        // consumer's OpenTelemetry does. It is what inherits the traceparent header.
        using var hosting = new ActivityListener
        {
            ShouldListenTo = static source => string.Equals(source.Name, "Microsoft.AspNetCore", StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };

        ActivitySource.AddActivityListener(hosting);

        const string traceparent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";

        var runs = await Task.WhenAll(
            RunAsync(host, "flow-a", traceparent),
            RunAsync(host, "flow-b", traceparent));

        var batchA = store.Batches.Single(batch => batch.RunId == runs[0]);
        var batchB = store.Batches.Single(batch => batch.RunId == runs[1]);

        batchA.TraceId.ShouldBe(batchB.TraceId, "the scenario needs one trace id for both runs");
        batchA.Spans.ShouldContain(static span => IsExecutorSpan(span) && span.Name.EndsWith("alpha-step", StringComparison.Ordinal));
        batchA.Spans.ShouldNotContain(static span => span.Name.EndsWith("beta-step", StringComparison.Ordinal), "flow-b's span landed in flow-a's run");
        batchB.Spans.ShouldContain(static span => IsExecutorSpan(span) && span.Name.EndsWith("beta-step", StringComparison.Ordinal));
        batchB.Spans.ShouldNotContain(static span => span.Name.EndsWith("alpha-step", StringComparison.Ordinal), "flow-a's span landed in flow-b's run");
    }

    [Fact]
    public async Task A_canceled_workflow_run_leaves_no_open_buffer()
    {
        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder.UseWorkflows();
                builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "hang",
                    static _ => async (_, _, token) =>
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, token); // delay: simulated

                        return [];
                    });
                builder.AddWorkflow(
                    "hanging-flow",
                    static services =>
                    {
                        // One binding for both roles: the framework refuses a second instance under the same id.
                        var hang = services.GetWorkflowFunction("hang");

                        return new WorkflowBuilder(hang)
                            .WithName("hanging-flow")
                            .WithOutputFrom(hang)
                            .WithOpenTelemetry()
                            .Build();
                    });
            },
            configureServices: static services => services.Configure<TraconOptions>(
                static options => options.Observability.SuccessSampleRatio = 1));

        var collector = host.Services.GetRequiredService<RunTraceCollector>();

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/tracon/api/workflows/hanging-flow/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new WorkflowRunHttpRequest { Message = "go" }),
        };

        using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        var frames = SseReader.ReadAsync(stream, TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        try
        {
            while (await frames.MoveNextAsync())
            {
                if (!string.Equals(frames.Current.Event, "run", StringComparison.Ordinal))
                {
                    continue;
                }

                var runId = JsonDocument.Parse(frames.Current.Data).RootElement.GetProperty("runId").GetGuid();

                // The endpoint announces the run id before the runner registers the run for cancellation and opens
                // its span buffer, so on a loaded machine the first frame can arrive a moment ahead of both.
                await WaitUntil.TrueAsync(() => collector.OpenRunCount == 1, "the run in flight holds its buffer");

                var cancel = await WaitUntil.ValueAsync(
                    async () =>
                    {
                        using var response = await host.Client.PostAsync(
                            new Uri($"/tracon/api/runs/{runId}/cancel", UriKind.Relative),
                            content: null,
                            TestContext.Current.CancellationToken);

                        var detail = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

                        return (response.StatusCode, Detail: detail);
                    },
                    static answer => answer.StatusCode != HttpStatusCode.Conflict,
                    "the run is registered for cancellation");

                cancel.StatusCode.ShouldBe(HttpStatusCode.Accepted, cancel.Detail);
                break;
            }

            while (await frames.MoveNextAsync())
            {
                // Drain the stream: the run has ended when the response does.
            }
        }
        finally
        {
            await frames.DisposeAsync();
        }

        collector.OpenRunCount.ShouldBe(0, "a canceled run left its span buffer open");
    }

    private static bool IsExecutorSpan(TraceSpan span)
        => span.Name.StartsWith(ExecutorSpan, StringComparison.Ordinal);

    private static Task<TraconTestHost> StartStoredAsync(Action<TraconOptions>? configureOptions = null)
        => NodeUsageHost.StartAsync(
            builder => builder
                .AddModelProvider(NodeUsageHost.PricedProvider())
                .AddAgent(NodeUsageHost.Writer("writer"))
                .AddAgent(NodeUsageHost.Writer("editor"))
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("pass", static _ => static (messages, _, _) => new(messages)),
            configureOptions: options =>
            {
                options.Observability.SuccessSampleRatio = 1;
                configureOptions?.Invoke(options);
            });

    private static async Task<RunTrace> RunStoredAsync(TraconTestHost host)
    {
        await NodeUsageHost.SaveAsync(
            host,
            NodeUsageHost.Workflow,
            NodeUsageHost.Agent("writer"),
            NodeUsageHost.Function("pass"),
            NodeUsageHost.Agent("editor"));

        return await RunTraceReader.ReadAsync(host, await NodeUsageHost.RunAsync(host));
    }

    private static Task<TraconTestHost> StartCodeAsync(bool telemetry)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder.UseWorkflows();
                builder.AddWorkflow(CodeFlow, _ => CodeGraph(CodeFlow, "trace-step", telemetry));
            },
            configureServices: static services => services.Configure<TraconOptions>(
                static options => options.Observability.SuccessSampleRatio = 1));

    private static async Task<RunTrace> RunCodeAsync(TraconTestHost host)
        => await RunTraceReader.ReadAsync(host, await RunAsync(host, CodeFlow, traceparent: null));

    /// <summary>
    /// A one-step graph built in code, the way a consumer builds it. <paramref name="telemetry"/> is the consumer's
    /// choice to call <c>WithOpenTelemetry()</c>; no source is passed, so the framework writes to its own.
    /// </summary>
    private static Workflow CodeGraph(string name, string stepId, bool telemetry)
    {
        var step = ExecutorBindingExtensions.BindAsExecutor<List<ChatMessage>, string>(static _ => "done", id: stepId);
        var builder = new WorkflowBuilder(step).WithName(name).WithOutputFrom(step);

        return (telemetry ? builder.WithOpenTelemetry() : builder).Build();
    }

    private static async Task<Guid> RunAsync(TraconTestHost host, string workflow, string? traceparent)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/workflows/{workflow}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new WorkflowRunHttpRequest { Message = "go" }),
        };

        if (traceparent is not null)
        {
            request.Headers.Add("traceparent", traceparent);
        }

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));

        return JsonDocument.Parse(frames.First(static frame => string.Equals(frame.Event, "run", StringComparison.Ordinal)).Data)
            .RootElement.GetProperty("runId").GetGuid();
    }
}
