using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AGUI.Abstractions;
using Tracon.AGUI.FunctionalTests.Infrastructure;

namespace Tracon.AGUI.FunctionalTests;

/// <summary>
/// A front end that goes away stops its run (Phase 236).
/// </summary>
public sealed class AGUICancellationTests
{
    [Fact]
    public async Task A_front_end_that_disconnects_mid_run_stops_the_run_and_the_run_is_recorded_as_canceled()
    {
        var model = new HangingModelProvider("hang-model");
        await using var host = await AGUITestHost.StartAsync(builder => builder
            .AddModelProvider(model)
            .AddAgent(TestAgents.Definition("hang-agent", "hang-model")));

        var body = JsonSerializer.Serialize(AGUIInputs.User("t-cancel", "hello"), AGUIJsonSerializerContext.Default.RunAgentInput);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{AGUITestHost.Pattern}/hang-agent", UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, new MediaTypeHeaderValue("application/json")),
        };

        var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        // The stream began and the model was called: the run is in flight. Going away is the front end closing the response.
        await model.Started.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        response.Dispose();

        var runs = host.Services.GetRequiredService<IRunStore>();

        var terminal = await WaitUntil.ValueAsync(
            async () => (await runs.QueryRunsAsync(new RunQuery { SessionId = "t-cancel" }, TestContext.Current.CancellationToken)).SingleOrDefault(),
            static run => run is { Status: RunStatus.Canceled or RunStatus.Completed or RunStatus.Failed },
            "the disconnected run to reach a terminal status");

        terminal!.Status.ShouldBe(RunStatus.Canceled, "a run nobody listens to is cancelled, never left running and never recorded as completed");
    }
}
