using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Tests.Common;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A model provider that misbehaves ends the run in a terminal state, with an error that does not echo the provider's
/// body, on the buffered path and on the streamed path alike.
/// </summary>
/// <remarks>
/// <para>
/// The provider is a local server that answers over HTTP the way a broken or hostile one can: an empty answer, a tool
/// call the agent does not have, tool arguments that are not JSON, two calls under one id, token counts below zero or
/// missing, a status the client retries, a stream cut after its first chunk, a stream that never says it finished, a
/// surrogate pair split over two chunks, an answer of several megabytes, an HTML page where JSON was promised.
/// The real SDK client reads it, so a shape the adapter handles differently after an upgrade fails here and not in a
/// user's run.
/// </para>
/// <para>
/// What every case must hold: the request is answered (no exception reaches the caller, no hang), the run is
/// <c>Completed</c> or <c>Failed</c> and never left <c>Running</c>, the token counts it recorded are not below zero
/// (a negative count times a price is money handed back), and a failure's text does not contain the provider's body,
/// which can carry anything.
/// </para>
/// <para>
/// The host runs on a <see cref="ManualClock"/>. The client library retries a status or a cut connection three times
/// with a backoff of its own (0.8, 1.6 and 3.2 seconds), and it waits on the host's clock, so the test steps the clock
/// through the waits instead of sitting through them. The steps are also asserted: a case the library retries must
/// have waited those steps, and one it does not retry must have waited none.
/// </para>
/// </remarks>
public sealed class UpstreamMisbehaviorTests
{
    private const string AgentName = "upstream-agent";
    private const string ProviderName = "upstream-provider";
    private const string Canary = "UPSTREAM-BODY-CANARY-71e3";
    private const string CutAfterFirstChunkCase = "a stream cut after its first chunk";

    private static readonly Uri RunUri = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);

    // The furthest timer the clock is stepped to. The library's longest backoff step is 3.2 seconds. The host's nearest
    // timer of its own is the job worker's poll, 10 seconds away when the call starts and still 4.2 seconds away after
    // the three waits of a call that never recovers (5.8 seconds), so a reach in between leaves it alone. A reach that
    // took it in would step it as soon as the last answer was in flight and no retry timer was armed.
    private static readonly TimeSpan RetryReach = TimeSpan.FromSeconds(3.5);

    private static readonly TimeSpan[] LibraryBackoff = [TimeSpan.FromSeconds(0.8), TimeSpan.FromSeconds(1.6), TimeSpan.FromSeconds(3.2)];

    public static TheoryData<string> Cases() => [.. Replies.Keys];

    private static IReadOnlyDictionary<string, Func<HttpContext, int, bool, Task>> Replies { get; } =
        new Dictionary<string, Func<HttpContext, int, bool, Task>>(StringComparer.Ordinal)
        {
            ["an empty answer"] = (context, call, streamed) => Answer(context, streamed, string.Empty),
            ["a tool call the agent does not have"] = (context, call, streamed)
                => call == 1 ? ToolCalls(context, streamed, ("call_1", "no_such_tool", "{}")) : Answer(context, streamed, "done"),
            ["tool arguments that are not JSON"] = (context, call, streamed)
                => call == 1 ? ToolCalls(context, streamed, ("call_1", "no_such_tool", "{\"orderId\": ")) : Answer(context, streamed, "done"),
            ["two tool calls under one id"] = (context, call, streamed)
                => call == 1
                    ? ToolCalls(context, streamed, ("call_1", "first_tool", "{}"), ("call_1", "second_tool", "{}"))
                    : Answer(context, streamed, "done"),
            ["token counts below zero"] = (context, call, streamed) => Answer(context, streamed, "ok", usage: "{\"prompt_tokens\":-5,\"completion_tokens\":-3,\"total_tokens\":-8}"),
            ["token counts that are null"] = (context, call, streamed) => Answer(context, streamed, "ok", usage: "{\"prompt_tokens\":null,\"completion_tokens\":null,\"total_tokens\":null}"),
            ["a 429 with Retry-After"] = (context, call, streamed) => Status(context, 429, $"{{\"error\":{{\"message\":\"{Canary}\"}}}}", retryAfter: "1"),
            ["a 500 with a body"] = (context, call, streamed) => Status(context, 500, $"{{\"error\":{{\"message\":\"{Canary}\"}}}}"),
            ["an HTML error page"] = (context, call, streamed) => Status(context, 502, $"<html><body>{Canary}</body></html>", contentType: "text/html"),
            ["a 200 whose body is not JSON"] = (context, call, streamed) => Status(context, 200, $"<html>{Canary}</html>", contentType: "text/html"),
            [CutAfterFirstChunkCase] = (context, call, streamed) => CutAfterFirstChunk(context, streamed),
            ["a stream that never says it finished"] = (context, call, streamed) => NeverFinishes(context, streamed),
            ["a surrogate pair split over two chunks"] = (context, call, streamed) => SplitSurrogate(context, streamed),
            ["an answer of three megabytes"] = (context, call, streamed) => Answer(context, streamed, new string('a', 3 * 1024 * 1024)),
        };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_buffered_run_against_a_misbehaving_provider_ends_in_a_terminal_state(string name)
    {
        var clock = new ManualClock();
        await using var server = await StartServerAsync(name);
        await using var host = await StartHostAsync(server, clock);

        var exchange = SendBufferedAsync(host);
        await clock.AdvanceUntilAsync(exchange, within: RetryReach);
        var (status, body) = await exchange;

        status.ShouldNotBe(HttpStatusCode.InternalServerError, $"{name}: the host answered with a bare 500");
        body.ShouldNotContain(Canary, Case.Sensitive, $"{name}: the response echoes the provider's body");
        clock.Steps.ShouldBe(ExpectedWaits(name, streamed: false), $"{name}: the waits between the client's tries");

        await AssertRunSettledAsync(host, name);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_streamed_run_against_a_misbehaving_provider_ends_in_a_terminal_state(string name)
    {
        var clock = new ManualClock();
        await using var server = await StartServerAsync(name);
        await using var host = await StartHostAsync(server, clock);

        var exchange = SendStreamedAsync(host);
        await clock.AdvanceUntilAsync(exchange, within: RetryReach);
        var frames = await exchange;

        frames.ShouldNotBeEmpty($"{name}: the stream carried no frame at all");
        string.Join('\n', frames.Select(static frame => frame.Data)).ShouldNotContain(Canary, Case.Sensitive, $"{name}: a frame echoes the provider's body");
        AssertStreamedWaits(clock, name);

        await AssertRunSettledAsync(host, name);
    }

    private static async Task<(HttpStatusCode Status, string Body)> SendBufferedAsync(TraconTestHost host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, RunUri)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        return (response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<IReadOnlyList<SseFrame>> SendStreamedAsync(TraconTestHost host)
    {
        using var response = await host.Client.PostAsJsonAsync(
            RunUri, new AgentRunRequest { Message = "hello" }, TestContext.Current.CancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);

        return await SseReader.ReadAllAsync(stream);
    }

    // What the client library does with the case: a status it retries or a connection cut before any answer gets three
    // retries at its ordinary backoff (a Retry-After within the ceiling replaces a step that is shorter than it); a
    // stream cut after its first chunk is not a try that failed, so nothing is retried.
    // A stream the server cuts right after its first chunk reaches the client in one of two ways, and which one depends on
    // the wire, not on the product: the client has the response head when the connection ends (a stream broken after
    // bytes arrived: no retry, no wait), or the reset is first (a connection error before any answer: the library
    // retries, and waits its backoff as far as the tries it gets before one of them delivers the head). Both end the run
    // in a terminal state, which is what the case holds; the waits are then none or a prefix of the library's backoff.
    private static void AssertStreamedWaits(ManualClock clock, string name)
    {
        if (string.Equals(name, CutAfterFirstChunkCase, StringComparison.Ordinal))
        {
            clock.Steps.ShouldBe(LibraryBackoff.Take(clock.Steps.Count), $"{name}: no wait, or the start of the library's backoff");

            return;
        }

        clock.Steps.ShouldBe(ExpectedWaits(name, streamed: true), $"{name}: the waits between the client's tries");
    }

    private static TimeSpan[] ExpectedWaits(string name, bool streamed) => name switch
    {
        "a 429 with Retry-After" => [TimeSpan.FromSeconds(1), LibraryBackoff[1], LibraryBackoff[2]],
        "a 500 with a body" or "an HTML error page" => LibraryBackoff,
        CutAfterFirstChunkCase => streamed ? [] : LibraryBackoff,
        _ => [],
    };

    private static async Task AssertRunSettledAsync(TraconTestHost host, string name)
    {
        var runs = await host.Client.GetFromJsonAsync<List<RunRecord>>(
            new Uri("/tracon/api/runs", UriKind.Relative), TestContext.Current.CancellationToken);

        var run = await host.WaitForTerminalRunAsync(runs.ShouldHaveSingleItem().Id);

        run.Status.ShouldBeOneOf([RunStatus.Completed, RunStatus.Failed], $"{name}: the run did not settle");

        if (run.Usage is { } usage)
        {
            (usage.InputTokens ?? 0).ShouldBeGreaterThanOrEqualTo(0, $"{name}: the recorded input tokens are below zero");
            (usage.OutputTokens ?? 0).ShouldBeGreaterThanOrEqualTo(0, $"{name}: the recorded output tokens are below zero");
        }

        if (run.Status == RunStatus.Failed)
        {
            run.Error.ShouldNotBeNull($"{name}: a failed run records no error");
            run.Error.Message.ShouldNotContain(Canary, Case.Sensitive, $"{name}: the recorded error echoes the provider's body");
        }
    }

    private static async Task<FakeOpenAiCompatibleServer> StartServerAsync(string name)
    {
        var server = await FakeOpenAiCompatibleServer.StartAsync();
        var reply = Replies[name];

        server.ChatCompletionOverride = (context, call) =>
            reply(context, call, IsStreamed(server.ChatCompletionBodies[^1]));

        return server;
    }

    private static Task<TraconTestHost> StartHostAsync(FakeOpenAiCompatibleServer server, ManualClock clock)
        => TraconTestHost.StartAsync(
            configureServices: services => services.AddSingleton<TimeProvider>(clock),
            configureTracon: builder => builder
            .UseOpenAICompatible(ProviderName, options => options.Endpoint = server.BaseAddress)
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Reply briefly.",
                Model = new ModelBinding { Provider = ProviderName, Model = "fake-local-model" },
                Origin = AgentDefinitionOrigin.Code,
            }));

    private static bool IsStreamed(System.Text.Json.Nodes.JsonObject body)
        => body.TryGetPropertyValue("stream", out var value) && value is not null && value.GetValue<bool>();

    private static async Task Answer(HttpContext context, bool streamed, string text, string usage = "{\"prompt_tokens\":5,\"completion_tokens\":3,\"total_tokens\":8}")
    {
        if (!streamed)
        {
            await Json(context, 200, $"{{\"id\":\"c\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"m\",\"choices\":[{{\"index\":0,\"message\":{{\"role\":\"assistant\",\"content\":{JsonSerializer.Serialize(text)}}},\"finish_reason\":\"stop\"}}],\"usage\":{usage}}}");

            return;
        }

        await Sse(
            context,
            Chunk($"{{\"role\":\"assistant\",\"content\":{JsonSerializer.Serialize(text)}}}", finish: null),
            Chunk("{}", finish: "stop"),
            $"{{\"id\":\"c\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"m\",\"choices\":[],\"usage\":{usage}}}");
    }

    private static async Task ToolCalls(HttpContext context, bool streamed, params (string Id, string Name, string Arguments)[] calls)
    {
        var json = string.Join(
            ',',
            calls.Select((call, index) =>
                $"{{\"index\":{index},\"id\":\"{call.Id}\",\"type\":\"function\",\"function\":{{\"name\":\"{call.Name}\",\"arguments\":{JsonSerializer.Serialize(call.Arguments)}}}}}"));

        if (!streamed)
        {
            await Json(context, 200, $"{{\"id\":\"c\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"m\",\"choices\":[{{\"index\":0,\"message\":{{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{json}]}},\"finish_reason\":\"tool_calls\"}}],\"usage\":{{\"prompt_tokens\":5,\"completion_tokens\":3,\"total_tokens\":8}}}}");

            return;
        }

        await Sse(
            context,
            Chunk($"{{\"role\":\"assistant\",\"tool_calls\":[{json}]}}", finish: null),
            Chunk("{}", finish: "tool_calls"));
    }

    private static async Task CutAfterFirstChunk(HttpContext context, bool streamed)
    {
        if (!streamed)
        {
            // A buffered body announced and not delivered: the connection ends halfway through it.
            context.Response.ContentType = "application/json";
            context.Response.ContentLength = 4096;
            await context.Response.WriteAsync("{\"id\":\"c\",\"choices\":[", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            context.Abort();

            return;
        }

        context.Response.ContentType = "text/event-stream";
        await context.Response.WriteAsync($"data: {Chunk("{\"role\":\"assistant\",\"content\":\"par\"}", finish: null)}\n\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
        context.Abort();
    }

    private static Task NeverFinishes(HttpContext context, bool streamed)
        => streamed
            ? Sse(context, Chunk("{\"role\":\"assistant\",\"content\":\"unfinished\"}", finish: null), done: false)
            : Json(context, 200, "{\"id\":\"c\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"m\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"unfinished\"},\"finish_reason\":null}]}");

    private static Task SplitSurrogate(HttpContext context, bool streamed)
        => streamed
            ? Sse(
                context,
                Chunk("{\"role\":\"assistant\",\"content\":\"\\ud83d\"}", finish: null),
                Chunk("{\"content\":\"\\ude00\"}", finish: null),
                Chunk("{}", finish: "stop"))
            : Answer(context, streamed: false, "\U0001F600");

    private static async Task Status(HttpContext context, int status, string body, string contentType = "application/json", string? retryAfter = null)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = contentType;

        if (retryAfter is not null)
        {
            context.Response.Headers.RetryAfter = retryAfter;
        }

        await context.Response.WriteAsync(body, Encoding.UTF8, context.RequestAborted);
    }

    private static Task Json(HttpContext context, int status, string json) => Status(context, status, json);

    private static string Chunk(string delta, string? finish)
        => $"{{\"id\":\"c\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"m\",\"choices\":[{{\"index\":0,\"delta\":{delta},\"finish_reason\":{(finish is null ? "null" : "\"" + finish + "\"")}}}]}}";

    private static async Task Sse(HttpContext context, params string[] chunks) => await Sse(context, chunks, done: true);

    private static async Task Sse(HttpContext context, string chunk, bool done) => await Sse(context, [chunk], done);

    private static async Task Sse(HttpContext context, string[] chunks, bool done)
    {
        context.Response.ContentType = "text/event-stream";

        foreach (var chunk in chunks)
        {
            await context.Response.WriteAsync($"data: {chunk}\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }

        if (done)
        {
            await context.Response.WriteAsync("data: [DONE]\n\n", context.RequestAborted);
        }
    }
}
