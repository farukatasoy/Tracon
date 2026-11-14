using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A trace belongs to the run, not to the W3C trace id the caller sent: two runs that share an id read their own spans.
/// </summary>
/// <remarks>
/// <para>
/// A gateway that propagates distributed tracing hands every run it starts the same trace id. The id names the caller's
/// request tree, so it cannot name one run's trace: when it did, the first run read <c>404</c> or the second run's spans, and
/// deleting one run took or kept the other run's spans.
/// </para>
/// <para>
/// 🚨 The run is real from the request to the response: a real host, the real collector, the real store and the real
/// <c>GET /api/runs/{id}/trace</c>. The store test of the contract proves the store; only this path proves the collector
/// hands the store the run that owns the spans.
/// </para>
/// </remarks>
public sealed class TraceSharedIdentityTests
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    private const string Traceparent = "00-" + TraceId + "-00f067aa0ba902b7-01";

    [Fact]
    public async Task Two_runs_that_send_the_same_traceparent_each_read_their_own_trace_over_HTTP()
    {
        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder.UseWorkflows();
                builder.AddWorkflow("flow-a", static _ => CodeGraph("flow-a", "alpha-step"));
                builder.AddWorkflow("flow-b", static _ => CodeGraph("flow-b", "beta-step"));
            },
            configureServices: static services => services.Configure<TraconOptions>(
                static options => options.Observability.SuccessSampleRatio = 1));

        // The server span of a request is created only while something listens to ASP.NET Core's source, as a
        // consumer's OpenTelemetry does. It is what inherits the traceparent header.
        using var hosting = new ActivityListener
        {
            ShouldListenTo = static source => string.Equals(source.Name, "Microsoft.AspNetCore", StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };

        ActivitySource.AddActivityListener(hosting);

        var first = await RunAsync(host, "flow-a");
        var second = await RunAsync(host, "flow-b");

        first.ShouldNotBe(second);

        var traceFirst = await RunTraceReader.ReadAsync(host, first);
        var traceSecond = await RunTraceReader.ReadAsync(host, second);

        traceFirst.TraceId.ShouldBe(TraceId, "the scenario needs the caller's id on both runs");
        traceSecond.TraceId.ShouldBe(TraceId, "the scenario needs the caller's id on both runs");
        traceFirst.RunId.ShouldBe(first);
        traceSecond.RunId.ShouldBe(second);
        traceFirst.Id.ShouldNotBe(traceSecond.Id);

        traceFirst.Spans.ShouldContain(static span => span.Name.EndsWith("alpha-step", StringComparison.Ordinal));
        traceFirst.Spans.ShouldNotContain(static span => span.Name.EndsWith("beta-step", StringComparison.Ordinal), "the second run's span landed in the first run's trace");
        traceSecond.Spans.ShouldContain(static span => span.Name.EndsWith("beta-step", StringComparison.Ordinal));
        traceSecond.Spans.ShouldNotContain(static span => span.Name.EndsWith("alpha-step", StringComparison.Ordinal), "the first run's span landed in the second run's trace");
    }

    private static Workflow CodeGraph(string name, string stepId)
    {
        var step = ExecutorBindingExtensions.BindAsExecutor<List<ChatMessage>, string>(static _ => "done", id: stepId);

        return new WorkflowBuilder(step).WithName(name).WithOutputFrom(step).WithOpenTelemetry().Build();
    }

    private static async Task<Guid> RunAsync(TraconTestHost host, string workflow)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/workflows/{workflow}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new WorkflowRunHttpRequest { Message = "go" }),
        };

        request.Headers.Add("traceparent", Traceparent);

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));

        return JsonDocument.Parse(frames.First(static frame => string.Equals(frame.Event, "run", StringComparison.Ordinal)).Data)
            .RootElement.GetProperty("runId").GetGuid();
    }
}
