using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Reasoning recording over the whole stack: options as the host configured
/// them, the compiled agent, the run recorder and the events a caller can read
/// back.
/// </summary>
/// <remarks>
/// 🚨 <c>ReasoningRecordingTests</c> already pinned the recorder's own switch,
/// and it was green while a real installation reported zero
/// <c>ReasoningDelta</c> events for four real provider calls (HATA-S3-006).
/// That defect turned out not to be in the code — the setting was off — but
/// the reason nobody could tell is that no test covered the distance between
/// "the switch works" and "an installation with this setting records this".
/// These tests cover that distance, from both directions: on records, off does
/// not, and the report says which.
/// </remarks>
public sealed class ReasoningRecordingEndpointTests
{
    [Fact]
    public async Task Reasoning_content_is_recorded_when_the_installation_turns_it_on()
    {
        await using var host = await StartAsync(recordReasoning: true);

        var events = await RunAndReadEventsAsync(host);

        var reasoning = events.Where(static e => e.Type == RunEventType.ReasoningDelta).ShouldHaveSingleItem();
        reasoning.Text.ShouldBe("thinking it through");

        // Separate event, never folded into the answer.
        events.Where(static e => e.Type == RunEventType.MessageDelta)
            .ShouldHaveSingleItem()
            .Text.ShouldBe("the answer");
    }

    [Fact]
    public async Task Reasoning_content_leaves_no_event_when_the_installation_leaves_it_off()
    {
        // The shape a reader has to be able to recognise: the model DID reason,
        // the record simply does not carry it. Nothing else in the run looks
        // different, which is why the report below exists.
        await using var host = await StartAsync(recordReasoning: false);

        var events = await RunAndReadEventsAsync(host);

        events.ShouldNotContain(static e => e.Type == RunEventType.ReasoningDelta);
        events.ShouldContain(static e => e.Type == RunEventType.MessageDelta);
    }

    [Fact]
    public async Task The_diagnostics_report_states_which_recording_settings_are_in_force()
    {
        // Without this, "the model did not reason", "recording is off" and
        // "recording is broken" are one indistinguishable outcome, and telling
        // them apart costs real provider calls.
        await using var host = await StartAsync(
            recordReasoning: true,
            configureEndpoints: static o => o.EnableDiagnosticsEndpoint = true);

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/api/diagnostics", UriKind.Relative));

        response.EnsureSuccessStatusCode();

        var recording = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("runRecording");

        recording.GetProperty("recordReasoningDeltas").GetBoolean().ShouldBeTrue();
        recording.GetProperty("recordMessageDeltas").GetBoolean().ShouldBeTrue();
        recording.GetProperty("enabled").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task The_diagnostics_report_says_so_when_reasoning_recording_is_off()
    {
        await using var host = await StartAsync(
            recordReasoning: false,
            configureEndpoints: static o => o.EnableDiagnosticsEndpoint = true);

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/api/diagnostics", UriKind.Relative));

        response.EnsureSuccessStatusCode();

        var recording = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("runRecording");

        recording.GetProperty("recordReasoningDeltas").GetBoolean().ShouldBeFalse();

        // The sibling is on by default, which is exactly why its working says
        // nothing about whether anything was configured at all.
        recording.GetProperty("recordMessageDeltas").GetBoolean().ShouldBeTrue();
    }

    private static Task<TraconTestHost> StartAsync(
        bool recordReasoning,
        Action<TraconEndpointOptions>? configureEndpoints = null)
        => TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .AddModelProvider(new ReasoningModelProvider())
                .AddAgent(TestData.Definition() with
                {
                    Model = new ModelBinding { Provider = "reasoner", Model = "kod-modeli" },
                }),
            configureEndpoints: configureEndpoints,
            configureServices: services => services.Configure<TraconOptions>(
                o => o.RunRecording.RecordReasoningDeltas = recordReasoning));

    private static async Task<List<RunEvent>> RunAndReadEventsAsync(TraconTestHost host)
    {
        using (var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/kod-agent/run", UriKind.Relative),
            new AgentRunRequest { Message = "hello" }))
        {
            response.EnsureSuccessStatusCode();
        }

        var runs = host.Services.GetRequiredService<IRunStore>();
        var run = (await runs.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        var events = new List<RunEvent>();
        await foreach (var runEvent in runs.ReadEventsAsync(run.Id))
        {
            events.Add(runEvent);
        }

        return events;
    }

    /// <summary>A provider whose model reasons before it answers.</summary>
    private sealed class ReasoningModelProvider : IModelProvider
    {
        public string Name => "reasoner";

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = "kod-modeli" }];

        public IChatClient CreateChatClient(ModelBinding binding) => new ReasoningChatClient();
    }

    private sealed class ReasoningChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                [new TextReasoningContent("thinking it through"), new TextContent("the answer")])));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("thinking it through")]);
            yield return new ChatResponseUpdate(ChatRole.Assistant, "the answer");

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // The fake client has no resources to release.
        }
    }
}
