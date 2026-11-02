using System.Net.Http.Json;
using System.Text;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A client that goes away while the model is still working leaves no run behind in a state that never ends.
/// </summary>
/// <remarks>
/// <para>
/// Every route that starts a run is driven the same way: the model call blocks, the client cancels its request once
/// the call has started, and the test then reads the run list. A run still <c>Running</c> after the client is gone has
/// nobody to finish it, so it stays <c>Running</c> until a reconciler that is off by default closes it. The model here
/// honours cancellation, so a run that outlives its client does so because the host never told the model to stop.
/// </para>
/// </remarks>
public sealed class ClientDisconnectRunTests
{
    [Theory]
    [InlineData("/tracon/api/agents/kod-agent/run", "agent-stream")]
    [InlineData("/tracon/api/agents/kod-agent/run", "agent-json")]
    [InlineData("/tracon/v1/chat/completions", "chat-buffered")]
    [InlineData("/tracon/v1/chat/completions", "chat-stream")]
    [InlineData("/tracon/v1/responses", "responses-buffered")]
    [InlineData("/tracon/v1/responses", "responses-stream")]
    public async Task A_run_whose_client_has_gone_does_not_stay_Running(string route, string shape)
    {
        var hanging = new HangingModelProvider("hanging", respectsCancellation: true);
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddModelProvider(hanging)
                .AddAgent(TestData.Definition() with { Model = new ModelBinding { Provider = hanging.Name, Model = "hanging-1" } }));

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(route, UriKind.Relative))
        {
            Content = BodyFor(shape),
        };

        if (string.Equals(shape, "agent-json", StringComparison.Ordinal))
        {
            // A request that carries an idempotency key gets a JSON body instead of a stream.
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        }

        using var client = new CancellationTokenSource();
        var sending = SendUntilCancelledAsync(host, request, client.Token);

        await hanging.Started.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await client.CancelAsync();
        await sending;

        // The wait is for the host to notice, which takes a few scheduling turns, not a fixed time.
        var statuses = await WaitUntil.ValueAsync(
            async () => await RunStatusesAsync(host),
            static found => found.Count > 0 && !found.Contains("Running", StringComparer.Ordinal),
            $"the run behind '{shape}' to leave Running after its client went away");

        statuses.Contains("Running", StringComparer.Ordinal).ShouldBeFalse();

        // Control: the run ended because the host stopped it, not because it never began.
        statuses.Any(static status => status is "Canceled" or "Failed").ShouldBeTrue(
            $"the run behind '{shape}' left Running without reaching a terminal status: {string.Join(", ", statuses)}");
    }

    private static StringContent BodyFor(string shape) => shape switch
    {
        "chat-buffered" => Json("""{"model":"kod-agent","messages":[{"role":"user","content":"hello"}]}"""),
        "chat-stream" => Json("""{"model":"kod-agent","stream":true,"messages":[{"role":"user","content":"hello"}]}"""),
        "responses-buffered" => Json("""{"model":"kod-agent","input":"hello"}"""),
        "responses-stream" => Json("""{"model":"kod-agent","stream":true,"input":"hello"}"""),
        _ => Json("""{"message":"hello"}"""),
    };

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static async Task SendUntilCancelledAsync(TraconTestHost host, HttpRequestMessage request, CancellationToken cancellation)
    {
        try
        {
            using var reply = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
            _ = await reply.Content.ReadAsStringAsync(cancellation);
        }
        catch (OperationCanceledException)
        {
            // This is the client going away.
        }
    }

    private static async Task<List<string>> RunStatusesAsync(TraconTestHost host)
    {
        using var reply = await host.Client.GetAsync(new Uri("/tracon/api/runs", UriKind.Relative));
        var runs = await TraconTestHost.ReadJsonAsync(reply);

        return [.. runs.EnumerateArray().Select(static run => run.GetProperty("status").GetString() ?? string.Empty)];
    }
}
