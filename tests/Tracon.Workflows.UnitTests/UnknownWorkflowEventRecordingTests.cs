using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// F-589 (phase 223): a <c>WorkflowEvent</c> Tracon has no event type for is in the run
/// record as <see cref="RunEventCustomTypes.WorkflowEvent"/>, through the real runner and a
/// real function node. It used to be a warning in the log and nothing in the record.
/// </summary>
public sealed class UnknownWorkflowEventRecordingTests
{
    [Fact]
    public async Task A_code_workflows_own_event_is_in_the_stream_and_in_the_stored_record()
    {
        var host = await HostThatRaisesAsync();
        var runner = host.CreateRunner();

        var streamed = await CollectAsync(runner);
        var stored = await StoredAsync(host);

        foreach (var events in new[] { streamed, stored })
        {
            var notice = events
                .Where(static e => string.Equals(e.CustomType, RunEventCustomTypes.WorkflowEvent, StringComparison.Ordinal))
                .ShouldHaveSingleItem();

            notice.Type.ShouldBe(RunEventType.Custom);
            notice.Text.ShouldBe(nameof(OrderShippedEvent));

            using var payload = JsonDocument.Parse(notice.Payload!);
            payload.RootElement.GetProperty("eventType").GetString().ShouldBe(typeof(OrderShippedEvent).FullName);
            payload.RootElement.GetProperty("data").GetString().ShouldBe(nameof(OrderPayload));
        }
    }

    [Fact]
    public async Task The_event_takes_its_place_in_the_sequence_between_the_nodes_that_surround_it()
    {
        var host = await HostThatRaisesAsync();

        await CollectAsync(host.CreateRunner());

        var stored = await StoredAsync(host);

        stored.Select(static e => e.Sequence).ShouldBe(Enumerable.Range(0, stored.Count).Select(static i => (long)i));

        var index = stored.FindIndex(static e => e.CustomType is not null);
        var completedRaise = stored.FindIndex(static e =>
            e.Type == RunEventType.ExecutorCompleted && string.Equals(e.Text, "raise", StringComparison.Ordinal));

        index.ShouldBeGreaterThan(stored.FindIndex(static e =>
            e.Type == RunEventType.ExecutorInvoked && string.Equals(e.Text, "raise", StringComparison.Ordinal)));
        index.ShouldBeLessThan(completedRaise);
    }

    [Fact]
    public async Task The_run_still_completes_and_no_warning_is_logged()
    {
        var host = await HostThatRaisesAsync();
        var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        host.RunnerLogger = factory.CreateLogger<WorkflowRunner>();

        await CollectAsync(host.CreateRunner());

        var root = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(static run => run.Kind == RunKind.Workflow);

        root.Status.ShouldBe(RunStatus.Completed);
        logs.Entries.ShouldNotContain(static entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task With_payloads_off_the_event_and_its_text_stay_and_the_payload_goes()
    {
        var host = await HostThatRaisesAsync();
        host.TraconOptions.RunRecording.RecordToolPayloads = false;

        await CollectAsync(host.CreateRunner());

        var notice = (await StoredAsync(host))
            .Where(static e => string.Equals(e.CustomType, RunEventCustomTypes.WorkflowEvent, StringComparison.Ordinal))
            .ShouldHaveSingleItem();

        notice.Text.ShouldBe(nameof(OrderShippedEvent));
        notice.Payload.ShouldBeNull();
    }

    [Fact]
    public async Task An_event_whose_text_data_ends_in_a_cut_emoji_still_records_and_the_run_completes()
    {
        // Audit finding 1 (triaged as noise for its crash claim: the writer does not throw):
        // the cap fell inside the emoji and left half a pair. The run completes and the note
        // is written with the character dropped whole.
        var host = await HostThatRaisesAsync(new string('x', ContentEventPayload.MaxTextLength - 1) + "\U0001F600 and more");

        await CollectAsync(host.CreateRunner());

        var root = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(static run => run.Kind == RunKind.Workflow);

        root.Status.ShouldBe(RunStatus.Completed);

        var notice = (await StoredAsync(host))
            .Where(static e => string.Equals(e.CustomType, RunEventCustomTypes.WorkflowEvent, StringComparison.Ordinal))
            .ShouldHaveSingleItem();

        using var payload = JsonDocument.Parse(notice.Payload!);
        payload.RootElement.GetProperty("data").GetString()!.ShouldEndWith(RunEventWriter.TruncationMarker);
    }

    [Fact]
    public async Task The_events_payload_is_fitted_to_the_configured_payload_limit()
    {
        var host = await HostThatRaisesAsync(new string('\u4e2d', 1500));
        host.TraconOptions.RunRecording.MaxPayloadLength = 1024;

        await CollectAsync(host.CreateRunner());

        var notice = (await StoredAsync(host))
            .Where(static e => string.Equals(e.CustomType, RunEventCustomTypes.WorkflowEvent, StringComparison.Ordinal))
            .ShouldHaveSingleItem();

        notice.Payload!.Length.ShouldBeLessThanOrEqualTo(1024);

        using var payload = JsonDocument.Parse(notice.Payload!);
        payload.RootElement.GetProperty("data").GetString()!.ShouldEndWith(RunEventWriter.TruncationMarker);
    }

    private static async Task<WorkflowTestHost> HostThatRaisesAsync(object? data = null)
    {
        var host = new WorkflowTestHost("one");

        host.AddFunction<List<ChatMessage>, List<ChatMessage>>(
            "raise",
            async (messages, context, cancellationToken) =>
            {
                await context.AddEventAsync(new OrderShippedEvent(data ?? new OrderPayload()), cancellationToken).ConfigureAwait(false);

                return messages;
            });

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            Nodes =
            [
                new WorkflowNodeReference { Name = "one", Kind = WorkflowNodeKind.Agent },
                new WorkflowNodeReference { Name = "raise", Kind = WorkflowNodeKind.Function },
            ],
        });

        return host;
    }

    private static async Task<List<RunEvent>> CollectAsync(WorkflowRunner runner)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in runner.RunStreamingAsync(new WorkflowRunRequest
        {
            WorkflowName = "chain",
            Message = "hi",
        }))
        {
            events.Add(runEvent);
        }

        return events;
    }

    private static async Task<List<RunEvent>> StoredAsync(WorkflowTestHost host)
    {
        var root = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(static run => run.Kind == RunKind.Workflow);

        var events = new List<RunEvent>();

        await foreach (var runEvent in host.RunStore.ReadEventsAsync(root.Id))
        {
            events.Add(runEvent);
        }

        return events;
    }

    private sealed class OrderShippedEvent(object data) : WorkflowEvent(data);

    private sealed class OrderPayload
    {
        public override string ToString() => "ORDER-7 for a@example.com";
    }
}
