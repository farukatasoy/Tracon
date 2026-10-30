using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>Runs an agent over HTTP and reads the span trace the store kept for the run.</summary>
internal static class RunTraceReader
{
    /// <summary>The span attribute that names what a Microsoft.Extensions.AI ring did.</summary>
    public const string OperationName = "gen_ai.operation.name";

    /// <summary>Runs <paramref name="agentName"/> once and returns the run's stored trace.</summary>
    /// <param name="host">The host.</param>
    /// <param name="agentName">The agent to run.</param>
    /// <param name="message">The user message.</param>
    /// <returns>The run id and the stored trace.</returns>
    public static async Task<(Guid RunId, RunTrace Trace)> RunAsync(TraconTestHost host, string agentName, string message = "go")
    {
        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/agents/{agentName}/run", UriKind.Relative),
            new AgentRunRequest { Message = message },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        var runId = JsonDocument.Parse(frames.Single(static frame => string.Equals(frame.Event, "run", StringComparison.Ordinal)).Data)
            .RootElement.GetProperty("runId").GetGuid();

        return (runId, await ReadAsync(host, runId));
    }

    /// <summary>Reads the stored trace of a run.</summary>
    /// <param name="host">The host.</param>
    /// <param name="runId">The run.</param>
    /// <returns>The trace.</returns>
    public static async Task<RunTrace> ReadAsync(TraconTestHost host, Guid runId)
    {
        using var response = await host.Client.GetAsync(
            new Uri($"/tracon/api/runs/{runId}/trace", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<RunTrace>(TestContext.Current.CancellationToken)).ShouldNotBeNull();
    }

    /// <summary>Returns the spans of a trace whose ring operation is <paramref name="operation"/>.</summary>
    /// <param name="trace">The trace.</param>
    /// <param name="operation">The <c>gen_ai.operation.name</c> value.</param>
    /// <returns>The spans.</returns>
    public static IReadOnlyList<TraceSpan> Operations(RunTrace trace, string operation)
        => [.. trace.Spans.Where(span => span.Attributes.TryGetValue(OperationName, out var value)
            && string.Equals(value, operation, StringComparison.Ordinal))];
}
