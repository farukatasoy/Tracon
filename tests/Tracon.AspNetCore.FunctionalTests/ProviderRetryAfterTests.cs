using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Http;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A provider's <c>Retry-After</c> never holds a run for longer than a minute, never crashes it, and a retry that
/// follows it is counted once.
/// </summary>
/// <remarks>
/// <para>
/// A provider that answers <c>429</c> says when to come back, and the header comes from the network. Both client
/// libraries (the OpenAI one behind <c>UseOpenAI</c>, <c>UseOpenAICompatible</c> and <c>UseAzureOpenAI</c>, and the
/// Anthropic one) retried on it without a ceiling. Measured against a local server before the fix: <c>86400</c>
/// held the request until the caller gave up (a day, had it waited), and a value past about 49 days, an HTTP date in
/// the next century or <c>2147483647</c> made the library's timer throw <see cref="ArgumentOutOfRangeException"/> out
/// of the retry loop, so the run failed with that exception instead of the provider's <c>429</c>.
/// </para>
/// <para>
/// The rule these tests hold: a delay up to a minute is obeyed, anything longer or not a delay is ignored in favor
/// of the library's ordinary backoff, the run reaches a terminal state, and the tokens of the one answer that came
/// back are counted once. Both modes are driven (buffered and streamed) because the retry sits in front of the
/// stream.
/// </para>
/// </remarks>
public sealed class ProviderRetryAfterTests
{
    private const string AgentName = "retry-after-agent";

    // A recovering run takes about a second and a run that never recovers about six (the library's three backoff
    // steps), so the bound is five times the slower one. A header that is obeyed blows through it.
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private static readonly Uri RunUri = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);

    public static TheoryData<string, string, bool> RecoveringRuns()
    {
        var data = new TheoryData<string, string, bool>();

        foreach (var provider in new[] { OpenAi, Anthropic })
        {
            foreach (var streamed in new[] { false, true })
            {
                foreach (var retryAfter in new[]
                {
                    "86400",
                    "Fri, 31 Dec 9999 23:59:59 GMT",
                    "2147483647",
                    "99999999999999999999",
                    "soon",
                    "-5",
                })
                {
                    data.Add(provider, retryAfter, streamed);
                }
            }
        }

        return data;
    }

    public static TheoryData<string, string, bool> RunsThatNeverRecover()
    {
        var data = new TheoryData<string, string, bool>();

        foreach (var provider in new[] { OpenAi, Anthropic })
        {
            foreach (var streamed in new[] { false, true })
            {
                data.Add(provider, "86400", streamed);
            }
        }

        return data;
    }

    public static TheoryData<string, bool> Providers()
    {
        var data = new TheoryData<string, bool>();

        foreach (var provider in new[] { OpenAi, Anthropic })
        {
            data.Add(provider, false);
            data.Add(provider, true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RecoveringRuns))]
    public async Task A_retry_after_that_is_absurd_or_not_a_delay_does_not_hold_or_fail_a_run_that_recovers(
        string provider,
        string retryAfter,
        bool streamed)
    {
        await using var rig = await StartAsync(provider, retryAfter, rejected: 1);
        using var bound = BoundedToken();

        await SendAsync(rig, streamed, bound.Token);

        var run = await SingleTerminalRunAsync(rig.Host);

        run.Status.ShouldBe(RunStatus.Completed, $"{provider}, Retry-After '{retryAfter}': {run.Error?.Message}");
        rig.Calls().ShouldBe(2, "one 429 and the retry that answered");
        run.Usage.ShouldNotBeNull();
        run.Usage.InputTokens.ShouldBe(rig.Input, "the tokens of the answer that came back are counted once");
        run.Usage.OutputTokens.ShouldBe(rig.Output);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task A_retry_after_within_a_minute_is_obeyed(string provider, bool streamed)
    {
        await using var rig = await StartAsync(provider, "2", rejected: 1);
        using var bound = BoundedToken();
        var clock = Stopwatch.StartNew();

        await SendAsync(rig, streamed, bound.Token);

        clock.Stop();

        var run = await SingleTerminalRunAsync(rig.Host);

        run.Status.ShouldBe(RunStatus.Completed, $"{provider}: {run.Error?.Message}");
        rig.Calls().ShouldBe(2);

        // The ordinary backoff of the first retry is under a second, so a wait of nearly two seconds is the header.
        clock.Elapsed.ShouldBeGreaterThan(TimeSpan.FromSeconds(1.5), $"{provider} did not wait for the Retry-After it was given");
    }

    [Theory]
    [MemberData(nameof(RunsThatNeverRecover))]
    public async Task A_provider_that_keeps_answering_429_with_an_absurd_retry_after_fails_the_run_with_that_429(
        string provider,
        string retryAfter,
        bool streamed)
    {
        await using var rig = await StartAsync(provider, retryAfter, rejected: int.MaxValue);
        using var bound = BoundedToken(TimeSpan.FromSeconds(60));

        await SendAsync(rig, streamed, bound.Token);

        var run = await SingleTerminalRunAsync(rig.Host);

        run.Status.ShouldBe(RunStatus.Failed);
        var message = run.Error.ShouldNotBeNull().Message;
        message.ShouldNotContain("ArgumentOutOfRange", Case.Sensitive, $"{provider}: the retry loop threw instead of failing with the provider's answer");
        message.ShouldContain("429", Case.Sensitive, $"{provider}: the failure is not the provider's 429");
        rig.Calls().ShouldBeGreaterThan(1, $"{provider}: the 429 was not retried with the ordinary backoff");
        (run.Usage?.InputTokens ?? 0).ShouldBe(0, "a run that never got an answer counts no tokens");
        (run.Usage?.OutputTokens ?? 0).ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_fallback_takes_over_from_a_provider_that_asks_for_a_day(bool streamed)
    {
        await using var primary = await FakeOpenAiCompatibleServer.StartAsync();
        await using var fallback = await FakeOpenAiCompatibleServer.StartAsync();

        primary.ChatCompletionOverride = (context, _) => TooManyRequests(context, "86400");
        fallback.ChatCompletionOverride = (context, _) => Answer(context, IsStreamed(fallback));

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .UseOpenAICompatible("retry-after-primary", options => options.Endpoint = primary.BaseAddress)
            .UseOpenAICompatible("retry-after-fallback", options => options.Endpoint = fallback.BaseAddress)
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Reply briefly.",
                Model = new ModelBinding
                {
                    Provider = "retry-after-primary",
                    Model = "fake-local-model",
                    Fallbacks = [new ModelFallback { Provider = "retry-after-fallback", Model = "fake-local-model" }],
                },
                Origin = AgentDefinitionOrigin.Code,
            }));

        using var bound = BoundedToken(TimeSpan.FromSeconds(60));

        await SendAsync(host, streamed, bound.Token);

        var run = await SingleTerminalRunAsync(host);

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);
        fallback.ChatCompletionCallCount.ShouldBe(1);
        primary.ChatCompletionCallCount.ShouldBeGreaterThan(1, "the primary's 429 was not retried before the fallback took over");
        run.Usage.ShouldNotBeNull();
        run.Usage.InputTokens.ShouldBe(5, "only the answer of the fallback is counted");
        run.Usage.OutputTokens.ShouldBe(3);
    }

    private const string OpenAi = "openai-compatible";

    private const string Anthropic = "anthropic";

    private static CancellationTokenSource BoundedToken(TimeSpan? bound = null)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        source.CancelAfter(bound ?? Bound);

        return source;
    }

    private static Task SendAsync(Rig rig, bool streamed, CancellationToken cancellationToken)
        => SendAsync(rig.Host, streamed, cancellationToken);

    private static async Task SendAsync(TraconTestHost host, bool streamed, CancellationToken cancellationToken)
    {
        if (!streamed)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, RunUri)
            {
                Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }),
            };
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

            using var buffered = await host.Client.SendAsync(request, cancellationToken);
            await buffered.Content.ReadAsStringAsync(cancellationToken);

            return;
        }

        using var response = await host.Client.PostAsJsonAsync(RunUri, new AgentRunRequest { Message = "hello" }, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        _ = await SseReader.ReadAllAsync(stream);
    }

    private static async Task<RunRecord> SingleTerminalRunAsync(TraconTestHost host)
    {
        var runs = await host.Client.GetFromJsonAsync<List<RunRecord>>(
            new Uri("/tracon/api/runs", UriKind.Relative), TestContext.Current.CancellationToken);

        return await host.WaitForTerminalRunAsync(runs.ShouldHaveSingleItem().Id);
    }

    private static async Task<Rig> StartAsync(string provider, string retryAfter, int rejected)
        => string.Equals(provider, Anthropic, StringComparison.Ordinal)
            ? await StartAnthropicAsync(retryAfter, rejected)
            : await StartOpenAiAsync(retryAfter, rejected);

    private static async Task<Rig> StartOpenAiAsync(string retryAfter, int rejected)
    {
        var server = await FakeOpenAiCompatibleServer.StartAsync();

        server.ChatCompletionOverride = (context, call)
            => call <= rejected ? TooManyRequests(context, retryAfter) : Answer(context, IsStreamed(server));

        var host = await TraconTestHost.StartAsync(builder => builder
            .UseOpenAICompatible("retry-after-provider", options => options.Endpoint = server.BaseAddress)
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Reply briefly.",
                Model = new ModelBinding { Provider = "retry-after-provider", Model = "fake-local-model" },
                Origin = AgentDefinitionOrigin.Code,
            }));

        return new Rig(host, server, () => server.ChatCompletionCallCount, Input: 5, Output: 3);
    }

    private static async Task<Rig> StartAnthropicAsync(string retryAfter, int rejected)
    {
        var server = await FakeAnthropicServer.StartAsync();

        server.MessagesOverride = async (context, call) =>
        {
            if (call <= rejected)
            {
                await TooManyRequests(context, retryAfter);

                return true;
            }

            var streamed = server.Bodies[call - 1].TryGetPropertyValue("stream", out var value) && value is not null && value.GetValue<bool>();

            context.Response.ContentType = streamed ? "text/event-stream" : "application/json";
            await context.Response.WriteAsync(streamed ? AnthropicStream : AnthropicMessage, context.RequestAborted);

            return true;
        };

        var host = await TraconTestHost.StartAsync(builder => builder
            .UseAnthropic("test-retry-after", options =>
            {
                options.Endpoint = server.BaseAddress;
                options.Models.Add(new ModelDescriptor { Name = "claude-test" });
            })
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Reply briefly.",
                Model = new ModelBinding { Provider = AnthropicProviderNames.Anthropic, Model = "claude-test", MaxOutputTokens = 1_000 },
                Origin = AgentDefinitionOrigin.Code,
            }));

        return new Rig(host, server, () => server.Bodies.Count, Input: 12, Output: 6);
    }

    private static bool IsStreamed(FakeOpenAiCompatibleServer server)
        => server.ChatCompletionBodies[^1].TryGetPropertyValue("stream", out var value) && value is not null && value.GetValue<bool>();

    private static async Task TooManyRequests(HttpContext context, string retryAfter)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.ContentType = "application/json";
        context.Response.Headers.RetryAfter = retryAfter;
        await context.Response.WriteAsync("{\"error\":{\"message\":\"slow down\"}}", Encoding.UTF8, context.RequestAborted);
    }

    private static async Task Answer(HttpContext context, bool streamed)
    {
        if (!streamed)
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                "{\"id\":\"c\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"m\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"ok\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":5,\"completion_tokens\":3,\"total_tokens\":8}}",
                context.RequestAborted);

            return;
        }

        context.Response.ContentType = "text/event-stream";

        foreach (var chunk in new[]
        {
            "{\"id\":\"c\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"m\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"ok\"},\"finish_reason\":null}]}",
            "{\"id\":\"c\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"m\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}",
            "{\"id\":\"c\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"m\",\"choices\":[],\"usage\":{\"prompt_tokens\":5,\"completion_tokens\":3,\"total_tokens\":8}}",
            "[DONE]",
        })
        {
            await context.Response.WriteAsync(string.Create(CultureInfo.InvariantCulture, $"data: {chunk}\n\n"), context.RequestAborted);
        }
    }

    private const string AnthropicMessage = """
        {"id":"msg_ok","type":"message","role":"assistant","model":"claude-test","content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":12,"output_tokens":6}}
        """;

    // The same message as the Messages API streams it: 12 input tokens at the start, 6 output tokens at the end.
    private const string AnthropicStream = """
        event: message_start
        data: {"type":"message_start","message":{"id":"msg_ok","type":"message","role":"assistant","model":"claude-test","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":12,"output_tokens":1}}}

        event: content_block_start
        data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"ok"}}

        event: content_block_stop
        data: {"type":"content_block_stop","index":0}

        event: message_delta
        data: {"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":6}}

        event: message_stop
        data: {"type":"message_stop"}


        """;

    private sealed record Rig(TraconTestHost Host, IAsyncDisposable Server, Func<int> Calls, int Input, int Output) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Host.DisposeAsync();
            await Server.DisposeAsync();
        }
    }
}
