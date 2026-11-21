using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AGUI.Abstractions;
using Tracon.AGUI.FunctionalTests.Infrastructure;

namespace Tracon.AGUI.FunctionalTests;

/// <summary>
/// A front end that goes away stops its run.
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

    [Fact]
    public async Task A_canceled_turn_the_model_had_started_to_answer_is_kept_in_the_history_when_the_host_asked_for_it()
    {
        const string partial = "The answer so far";
        var model = new StreamThenHangModelProvider("stream-hang", partial);

        await using var host = await AGUITestHost.StartAsync(
            builder => builder
                .AddModelProvider(model)
                .AddAgent(TestAgents.Definition("stream-agent", "stream-hang")),
            configureServices: static services => services.Configure<TraconSessionOptions>(static options => options.KeepCanceledTurns = true));

        var body = JsonSerializer.Serialize(AGUIInputs.User("t-keep", "what is the tide?"), AGUIJsonSerializerContext.Default.RunAgentInput);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{AGUITestHost.Pattern}/stream-agent", UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, new MediaTypeHeaderValue("application/json")),
        };

        var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        await model.ChunkSent.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // Reading the piece proves the server handled the update before the front end goes away.
        using (var reader = new StreamReader(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken)))
        {
            string? line;

            do
            {
                line = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
            }
            while (line is not null && !line.Contains(partial, StringComparison.Ordinal));

            line.ShouldNotBeNull("the partial answer reached the front end before it left");
        }

        response.Dispose();

        var agent = (await host.Services.GetRequiredService<IAgentCatalog>()
            .ResolveAsync("stream-agent", cancellationToken: TestContext.Current.CancellationToken)).ShouldNotBeNull();

        var kept = await WaitUntil.ValueAsync(
            async () => await host.Services.GetRequiredService<AgentSessionManager>()
                .ReadHistoryAsync(agent, "t-keep", cancellationToken: TestContext.Current.CancellationToken),
            static messages => messages is { Count: > 0 },
            "the canceled turn to be written to the session's history");

        kept!.Select(static message => (message.Role, message.Text)).ShouldBe(
            [
                (Microsoft.Extensions.AI.ChatRole.User, "what is the tide?"),
                (Microsoft.Extensions.AI.ChatRole.Assistant, partial + "\n\n[The response was canceled before it finished.]"),
            ]);
    }
}
