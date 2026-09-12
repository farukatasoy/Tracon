using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Verifies span persistence through the real DI, agent decoration, SSE, and HTTP endpoint chain.
/// </summary>
public sealed class RunTraceEndToEndTests
{
    [Fact]
    public async Task Successful_run_is_persisted_when_sample_ratio_is_one()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder.AddAgent(TestData.Definition("traced-agent")),
            configureServices: services => services.Configure<TraconOptions>(options =>
            {
                options.Observability.SuccessSampleRatio = 1;
            }));

        host.Services.GetRequiredService<RunTraceCollector>().IsCollecting.ShouldBeTrue();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/traced-agent/run", UriKind.Relative),
            new AgentRunRequest { Message = "hello" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());
        var runFrame = frames.Single(frame => string.Equals(frame.Event, "run", StringComparison.Ordinal));
        var runId = JsonDocument.Parse(runFrame.Data).RootElement.GetProperty("runId").GetGuid();

        using var traceResponse = await host.Client.GetAsync(
            new Uri($"/tracon/api/runs/{runId}/trace", UriKind.Relative),
            TestContext.Current.CancellationToken);

        traceResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var trace = await traceResponse.Content.ReadFromJsonAsync<RunTrace>(TestContext.Current.CancellationToken);
        trace.ShouldNotBeNull();
        trace.Spans.ShouldContain(span =>
            string.Equals(span.Name, TraconDiagnostics.RunActivityName, StringComparison.Ordinal));
    }
}
