using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// Same rationale as AgentExtensionRunTests.cs: a blanket `using Tracon.Testing;`
// would make TraconTestHost ambiguous.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The <see cref="RunEventCustomTypes.Context"/> event, through a real host and the real
/// agent — a context provider's messages are counted apart from the caller's own, the event is written once
/// for the run however many model calls the tool loop makes, and the ring that writes it sits where it sees
/// the provider's messages.
/// </summary>
/// <remarks>
/// The position of the ring is a structural claim: only the agent that runs its context providers before it
/// calls the chat client shows whether the ring sees what they added. A unit test with a hand-made request
/// cannot.
/// </remarks>
public sealed class ContextSourceRecordingTests
{
    private const string ProviderName = "context-model";
    private const string ModelName = "context-1";
    private const string AgentName = "context-agent";

    private static readonly Uri RunUri = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);

    [Fact]
    public async Task A_context_providers_messages_are_counted_apart_from_the_callers_own()
    {
        await using var host = await StartAsync(ToolProvider(), memory: 3);

        await RunAsync(host, "look it up");

        var context = await ContextEventsAsync(host);

        var notice = context.ShouldHaveSingleItem();
        var sources = SourcesOf(notice);

        notice.Text.ShouldBe("context: 4 messages");
        CountOf(sources, "External").ShouldBe(1);
        CountOf(sources, "AIContextProvider").ShouldBe(3);
    }

    [Fact]
    public async Task The_event_is_written_once_for_the_run_however_many_model_calls_the_tool_loop_makes()
    {
        var provider = ToolProvider();

        await using var host = await StartAsync(provider, memory: 2);

        await RunAsync(host, "look it up");

        // The loop asked the model twice: the question, then the tool result.
        provider.Requests.Count.ShouldBe(2);
        (await ContextEventsAsync(host)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_run_with_no_context_provider_counts_only_the_callers_messages()
    {
        await using var host = await StartAsync(ToolProvider(), memory: 0);

        await RunAsync(host, "look it up");

        var notice = (await ContextEventsAsync(host)).ShouldHaveSingleItem();

        notice.Text.ShouldBe("context: 1 messages");
        SourcesOf(notice).ShouldBe([("External", null, 1)]);
    }

    [Fact]
    public async Task The_payload_carries_counts_and_no_message_text()
    {
        await using var host = await StartAsync(ToolProvider(), memory: 2);

        await RunAsync(host, "a private question about order 4711");

        var notice = (await ContextEventsAsync(host)).ShouldHaveSingleItem();
        var payload = notice.Payload.ShouldNotBeNull();

        payload.ShouldNotContain("4711", Case.Sensitive);
        payload.ShouldNotContain("memory", Case.Sensitive);
    }

    [Fact]
    public async Task With_payloads_off_the_payload_stays_because_it_holds_counts_only()
    {
        await using var host = await StartAsync(
            ToolProvider(),
            memory: 2,
            static services => services.Configure<TraconOptions>(static options => options.RunRecording.RecordToolPayloads = false));

        await RunAsync(host, "look it up");

        var notice = (await ContextEventsAsync(host)).ShouldHaveSingleItem();

        notice.Text.ShouldBe("context: 3 messages");
        notice.Payload.ShouldNotBeNull();
        CountOf(SourcesOf(notice), "AIContextProvider").ShouldBe(2);
    }

    [Fact]
    public async Task An_extension_ring_that_answers_by_itself_does_not_take_the_record_away()
    {
        // The ring never calls inward: the model is not asked. The request was still built, and the
        // record of its composition is written above the ring.
        var provider = ToolProvider();

        await using var host = await StartAsync(
            provider,
            memory: 2,
            answeringRing: true);

        await RunAsync(host, "look it up");

        provider.Requests.ShouldBeEmpty("the ring answered, so the model was never asked");

        var notice = (await ContextEventsAsync(host)).ShouldHaveSingleItem();

        CountOf(SourcesOf(notice), "AIContextProvider").ShouldBe(2);
    }

    [Fact]
    public async Task A_second_turn_of_a_session_counts_the_stored_history_apart()
    {
        await using var host = await StartAsync(ToolProvider(), memory: 1);

        await RunAsync(host, "look it up", sessionId: "s-context");
        await RunAsync(host, "and again", sessionId: "s-context");

        var runs = await host.Client.GetFromJsonAsync<List<RunRecord>>(
            new Uri("/tracon/api/runs", UriKind.Relative), TestContext.Current.CancellationToken);
        var second = runs!.OrderBy(static r => r.StartedAt).Last();
        await host.WaitForTerminalRunAsync(second.Id);

        var notice = ContextEvents(await OutputTruncationTests.EventsAsync(host, second.Id)).ShouldHaveSingleItem();
        var sources = SourcesOf(notice);

        CountOf(sources, "External").ShouldBe(1);
        CountOf(sources, "AIContextProvider").ShouldBe(1);
        CountOf(sources, "ChatHistory").ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task The_live_stream_does_not_carry_the_context_event()
    {
        // The live run stream mirrors a closed list of notices. The context event is read from the
        // record, not pushed to every caller of the stream.
        await using var host = await StartAsync(ToolProvider(), memory: 1);

        var body = await RunAsync(host, "look it up");

        body.ShouldNotContain(RunEventCustomTypes.Context, Case.Sensitive);
    }

    private static async Task<string> RunAsync(TraconTestHost host, string message, string? sessionId = null)
    {
        using var response = await host.Client.PostAsJsonAsync(
            RunUri,
            new AgentRunRequest { Message = message, SessionId = sessionId },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldNotContain("event: error");

        return body;
    }

    private static async Task<List<RunEvent>> ContextEventsAsync(TraconTestHost host)
    {
        var run = await OutputTruncationTests.SingleRunAsync(host);

        return ContextEvents(await OutputTruncationTests.EventsAsync(host, run.Id));
    }

    private static List<RunEvent> ContextEvents(IEnumerable<RunEvent> events)
        => [.. events.Where(static e => string.Equals(e.CustomType, RunEventCustomTypes.Context, StringComparison.Ordinal))];

    private static List<(string Type, string? Id, int Count)> SourcesOf(RunEvent notice)
    {
        using var payload = JsonDocument.Parse(notice.Payload!);

        return [.. payload.RootElement.GetProperty("sources").EnumerateArray().Select(static source => (
            source.GetProperty("type").GetString()!,
            source.TryGetProperty("id", out var id) ? id.GetString() : null,
            source.GetProperty("count").GetInt32()))];
    }

    private static int CountOf(List<(string Type, string? Id, int Count)> sources, string type)
        => sources.Single(s => string.Equals(s.Type, type, StringComparison.Ordinal)).Count;

    private static FakeModelProvider ToolProvider()
        => new FakeModelProvider(ProviderName).ForModel(ModelName, static cfg => cfg.CallsTool("lookup").RespondsWith("found it"));

    private static Task<TraconTestHost> StartAsync(
        FakeModelProvider provider,
        int memory,
        Action<IServiceCollection>? configureServices = null,
        bool answeringRing = false)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(provider)
                    .AddTool((Func<string>)(static () => "42"), name: "lookup", description: "Looks the answer up.")
                    .AddAgentExtension("memory", context => AddContextProvider(context, new MemoryProvider(memory)))
                    .AddAgentExtension(
                        "answers",
                        static context => context.UseChatClient(ChatClientSlot.ModelCall, static inner => new AnsweringRing(inner)));

                builder.AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Answer from the lookup.",
                    Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
                    ToolNames = ["lookup"],
                    ExtensionNames = [.. (memory > 0 ? new[] { "memory" } : []), .. (answeringRing ? new[] { "answers" } : [])],
                    Origin = AgentDefinitionOrigin.Code,
                });
            },
            configureServices: configureServices);

    private static void AddContextProvider(AgentExtensionContext context, AIContextProvider provider)
    {
#pragma warning disable MAAI001 // HarnessAgentOptions members are marked "evaluation purposes only"
        if (context.ChatAgentOptions is { } chat)
        {
            chat.AIContextProviders = [.. chat.AIContextProviders ?? [], provider];
        }
#pragma warning restore MAAI001
    }

    /// <summary>A context provider that adds a fixed number of messages to every request, as memory does.</summary>
    private sealed class MemoryProvider(int count) : AIContextProvider
    {
        protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
            => new(new AIContext
            {
                Messages = [.. Enumerable.Range(1, count).Select(static i => new ChatMessage(ChatRole.User, $"memory {i}"))],
            });
    }

    /// <summary>A ring that returns an answer and never calls the client below it.</summary>
    private sealed class AnsweringRing(IChatClient inner) : DelegatingChatClient(inner)
    {
        public override Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answered by the ring")));

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "answered by the ring");
        }
    }
}
