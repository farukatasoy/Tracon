using System.Text;
using AGUI.Abstractions;
using AGUI.Client;
using Microsoft.Extensions.AI;
using Tracon.AGUI.FunctionalTests.Infrastructure;

namespace Tracon.AGUI.FunctionalTests;

/// <summary>
/// Reads the AG-UI endpoint with the protocol's official .NET client, the code a front end's .NET host (or a test
/// harness for CopilotKit) runs against it.
/// </summary>
/// <remarks>
/// The endpoint tests read the stream with the protocol SDK's event types. The official client goes further: it parses
/// every frame with its own formatter, runs the events through a state machine that rejects an out-of-order or
/// unterminated message, and turns the result into <c>IChatClient</c> updates. A stream the other tests accept can still
/// fail here, with an exception from the client's own validation.
/// </remarks>
public sealed class AGUIClientCompatibilityTests
{
    [Fact]
    public async Task A_text_run_reads_in_the_official_client_as_updates_whose_text_is_the_answer()
    {
        await using var host = await AGUITestHost.StartAsync(TestAgents.AddEcho);
        using var client = Client(host, TestAgents.Echo);

        var updates = new List<ChatResponseUpdate>();

        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello official client")], cancellationToken: TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        string.Concat(updates.Select(static update => update.Text)).ShouldBe("Echo: hello official client");
        updates.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task The_non_streaming_call_of_the_official_client_aggregates_the_same_answer()
    {
        await using var host = await AGUITestHost.StartAsync(TestAgents.AddEcho);
        using var client = Client(host, TestAgents.Echo);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "aggregate me")], cancellationToken: TestContext.Current.CancellationToken);

        response.Text.ShouldBe("Echo: aggregate me");
        response.Messages.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task The_official_stream_reader_decodes_every_frame_into_a_protocol_event_in_protocol_order()
    {
        await using var host = await AGUITestHost.StartAsync(TestAgents.AddEcho);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{AGUITestHost.Pattern}/{TestAgents.Echo}", UriKind.Relative))
        {
            Content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(AGUIInputs.User("t-reader", "ordered"), AGUIJsonSerializerContext.Default.RunAgentInput),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Accept.ParseAdd("text/event-stream");

        using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        var events = new List<BaseEvent>();

        await foreach (var item in response.ReadAGUIEventStreamAsync(TestContext.Current.CancellationToken))
        {
            events.Add(item);
        }

        events.ShouldNotBeEmpty();
        events[0].ShouldBeOfType<RunStartedEvent>();
        events[^1].ShouldBeOfType<RunFinishedEvent>();
        events.OfType<RunErrorEvent>().ShouldBeEmpty();

        // The text message is opened, filled and closed once, in that order, and under one message id.
        var message = events.Where(static e => e is TextMessageStartEvent or TextMessageContentEvent or TextMessageEndEvent).ToList();

        message[0].ShouldBeOfType<TextMessageStartEvent>();
        message[^1].ShouldBeOfType<TextMessageEndEvent>();
        message.OfType<TextMessageContentEvent>().ShouldNotBeEmpty();
        message.OfType<TextMessageContentEvent>().Select(static e => e.MessageId).Distinct(StringComparer.Ordinal).Count().ShouldBe(1);
        events.IndexOf(message[0]).ShouldBeGreaterThan(0, "the run starts before the message does");
        events.IndexOf(message[^1]).ShouldBeLessThan(events.Count - 1, "the message closes before the run finishes");
    }

    [Fact]
    public async Task A_failure_after_the_stream_began_reaches_the_official_client_as_an_error_not_as_a_clean_finish()
    {
        await using var host = await AGUITestHost.StartAsync(builder => builder
            .AddModelProvider(new FailingModelProvider("failing-model", "provider down"))
            .AddAgent(TestAgents.Definition("failing-agent", "failing-model")));

        using var client = Client(host, "failing-agent");

        var updates = new List<ChatResponseUpdate>();
        Exception? failure = null;

        try
        {
            await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken))
            {
                updates.Add(update);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            failure = exception;
        }

        var surfaced = failure is not null
            || updates.Any(static update => update.Contents.OfType<ErrorContent>().Any())
            || updates.Any(static update => update.FinishReason == ChatFinishReason.ContentFilter);

        surfaced.ShouldBeTrue("a RUN_ERROR must not look like a finished answer to the client");
    }

    [Fact]
    public async Task A_front_end_tool_round_trips_through_the_official_client_with_function_invocation()
    {
        await using var host = await AGUITestHost.StartAsync(builder => TestAgents.AddClientTool(builder));

        var invocations = 0;
        var openPage = AIFunctionFactory.Create(
            (string url) =>
            {
                Interlocked.Increment(ref invocations);

                return $"page at {url}";
            },
            name: TestAgents.OpenPage,
            description: "Opens a page in the user's browser.");

        using var inner = Client(host, TestAgents.ClientTool);
        using var client = new ChatClientBuilder(inner).UseFunctionInvocation().Build();

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "open the page")],
            new ChatOptions { Tools = [openPage] },
            TestContext.Current.CancellationToken);

        invocations.ShouldBe(1, "the model called the front-end tool once and the client ran it");
        response.Text.ShouldStartWith("Opened: ", Case.Sensitive);
        response.Text.ShouldContain("page at https://example.test", Case.Sensitive);
    }

    private static AGUIChatClient Client(AGUITestHost host, string agent)
        => new(new AGUIChatClientOptions(host.Client, new Uri(host.Client.BaseAddress!, $"{AGUITestHost.Pattern}/{agent}").ToString()));
}
