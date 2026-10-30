using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// Event mapping. The critical point is branch order: <c>AgentResponseEvent</c>
/// and <c>AgentResponseUpdateEvent</c> derive from <c>WorkflowOutputEvent</c>.
/// </summary>
public sealed class WorkflowEventMapperTests
{
    [Fact]
    public void Started_event_is_mapped()
    {
        var mapping = WorkflowEventMapper.Map(new WorkflowStartedEvent(message: null));

        mapping.IsKnown.ShouldBeTrue();
        mapping.Draft!.Value.Type.ShouldBe(RunEventType.WorkflowStarted);
    }

    [Fact]
    public void Agent_update_is_mapped_as_a_text_delta_NOT_as_output()
    {
        // Because AgentResponseUpdateEvent DERIVES from WorkflowOutputEvent, if the
        // general branch were written first this event would be classified as
        // "workflow produced output" and the real output would be lost.
        var update = new AgentResponseUpdate(ChatRole.Assistant, "chunk");
        var mapping = WorkflowEventMapper.Map(new AgentResponseUpdateEvent("writer", update));

        mapping.IsKnown.ShouldBeTrue();
        mapping.Draft!.Value.Type.ShouldBe(RunEventType.MessageDelta);
        mapping.Draft!.Value.Text.ShouldBe("chunk");
        mapping.Draft!.Value.ToolName.ShouldBe("writer");
    }

    [Fact]
    public void Full_agent_response_is_skipped()
    {
        // In a streaming run, updates already carry the text; also writing the
        // full response would show the same text twice in the stream.
        var response = new AgentResponse(new ChatMessage(ChatRole.Assistant, "full response"));
        var mapping = WorkflowEventMapper.Map(new AgentResponseEvent("writer", response));

        mapping.IsKnown.ShouldBeTrue();
        mapping.Draft.ShouldBeNull();
    }

    [Fact]
    public void Workflow_output_is_converted_to_text()
    {
        List<ChatMessage> output =
        [
            new(ChatRole.User, "question"),
            new(ChatRole.Assistant, "answer"),
        ];

        var mapping = WorkflowEventMapper.Map(new WorkflowOutputEvent(output, "OutputMessages"));

        mapping.Draft!.Value.Type.ShouldBe(RunEventType.WorkflowOutput);
        (mapping.Draft!.Value.Text ?? string.Empty).ShouldContain("answer", Case.Sensitive);
    }

    [Fact]
    public void Executor_error_is_mapped_with_the_safe_text_it_is_given()
    {
        // The mapper never reads an exception's own message; the runner
        // hands it the text it logged under a correlation id.
        var mapping = WorkflowEventMapper.Map(
            new ExecutorFailedEvent("writer", new InvalidOperationException("blew up")),
            "InvalidOperationException failed. (ref: 0a1b2c3d)");

        mapping.Draft!.Value.Type.ShouldBe(RunEventType.ExecutorFailed);
        mapping.Draft!.Value.Text.ShouldBe("writer");
        mapping.Draft!.Value.Payload.ShouldBe("InvalidOperationException failed. (ref: 0a1b2c3d)");
    }

    [Fact]
    public void Graph_error_is_mapped_with_the_safe_text_it_is_given()
    {
        var mapping = WorkflowEventMapper.Map(
            new WorkflowErrorEvent(new InvalidOperationException("internal host:9000")),
            "InvalidOperationException failed. (ref: 0a1b2c3d)");

        mapping.Draft!.Value.Type.ShouldBe(RunEventType.ExecutorFailed);
        mapping.Draft!.Value.Payload.ShouldBe("InvalidOperationException failed. (ref: 0a1b2c3d)");
    }

    [Fact]
    public void Super_step_events_carry_the_step_number()
    {
        var started = WorkflowEventMapper.Map(
            new SuperStepStartedEvent(3, new SuperStepStartInfo(["writer"])));

        started.Draft!.Value.Type.ShouldBe(RunEventType.SuperStepStarted);
        started.Draft!.Value.Text.ShouldBe("3");
        started.Draft!.Value.Payload.ShouldBe("writer");
    }

    [Fact]
    public void Unknown_event_is_marked_as_unrecognized_and_described_for_the_record()
    {
        // An event silently dropped would make debugging impossible the day MAF
        // adds a new type, or when a code workflow raises its own. It is recorded.
        var mapping = WorkflowEventMapper.Map(new UnknownEvent());

        mapping.IsKnown.ShouldBeFalse();

        var draft = mapping.Draft.ShouldNotBeNull();
        draft.Type.ShouldBe(RunEventType.Custom);
        draft.CustomType.ShouldBe(RunEventCustomTypes.WorkflowEvent);
        draft.Text.ShouldBe(nameof(UnknownEvent));
    }

    [Fact]
    public void An_unrecognized_event_names_its_full_type_and_leaves_out_what_it_does_not_have()
    {
        var draft = WorkflowEventMapper.DescribeUnrecognized(new UnknownEvent());

        using var payload = JsonDocument.Parse(draft.Payload!);
        payload.RootElement.GetProperty("eventType").GetString().ShouldBe(typeof(UnknownEvent).FullName);
        payload.RootElement.TryGetProperty("executorId", out _).ShouldBeFalse();
        payload.RootElement.GetProperty("data").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public void An_unrecognized_event_from_an_executor_names_the_executor()
    {
        var draft = WorkflowEventMapper.DescribeUnrecognized(new ExecutorProgressEvent("reviewer", data: null));

        using var payload = JsonDocument.Parse(draft.Payload!);
        payload.RootElement.GetProperty("executorId").GetString().ShouldBe("reviewer");
    }

    [Fact]
    public void An_unrecognized_events_text_data_is_written_and_other_data_only_by_type_name()
    {
        // The DescribeOutput rule: never ToString() of an arbitrary object, which would put a
        // field in the event table that was never meant to be stored.
        var text = WorkflowEventMapper.DescribeUnrecognized(new UnknownEvent("shipped order 7"));
        var message = WorkflowEventMapper.DescribeUnrecognized(
            new UnknownEvent(new ChatMessage(ChatRole.Assistant, "a chat message")));
        var other = WorkflowEventMapper.DescribeUnrecognized(new UnknownEvent(new SecretCarrier()));

        DataOf(text).ShouldBe("shipped order 7");
        DataOf(message).ShouldBe("a chat message");
        DataOf(other).ShouldBe(nameof(SecretCarrier));
        other.Payload!.ShouldNotContain("TOKEN-1234", Case.Sensitive);
    }

    [Fact]
    public void An_unrecognized_events_long_text_data_is_cut_so_the_payload_stays_valid_JSON()
    {
        var draft = WorkflowEventMapper.DescribeUnrecognized(
            new UnknownEvent(new string('x', ContentEventPayload.MaxTextLength * 4)));

        DataOf(draft).ShouldEndWith(RunEventWriter.TruncationMarker);
        draft.Payload!.Length.ShouldBeLessThan(new TraconRunRecordingOptions().MaxPayloadLength);
    }

    [Fact]
    public void An_unrecognized_events_text_data_cut_inside_a_surrogate_pair_does_not_throw()
    {
        // Audit finding 1: the cap fell between the two halves of an emoji. Measured:
        // Utf8JsonWriter does not throw, it writes U+FFFD, so the run was never at risk, but
        // the character was corrupted instead of dropped whole.
        var data = new string('x', ContentEventPayload.MaxTextLength - 1) + "\U0001F600 and more";

        var draft = WorkflowEventMapper.DescribeUnrecognized(new UnknownEvent(data));

        using var payload = JsonDocument.Parse(draft.Payload!);
        var cut = payload.RootElement.GetProperty("data").GetString()!;

        cut.ShouldEndWith(RunEventWriter.TruncationMarker);
        cut.ShouldNotContain("\uFFFD", Case.Sensitive, "the emoji is dropped whole, not left as half a pair");
    }

    [Fact]
    public void An_unrecognized_events_lone_surrogate_in_the_data_is_replaced_not_thrown_on()
    {
        var draft = WorkflowEventMapper.DescribeUnrecognized(new UnknownEvent("before \ud83d after"));

        DataOf(draft).ShouldBe("before \uFFFD after");
    }

    [Fact]
    public void An_unrecognized_events_payload_fits_the_writers_limit_with_text_that_escapes_long()
    {
        // Audit finding 2: every character outside ASCII costs six characters once escaped, so a
        // fixed cap in characters could still be cut by the writer in the middle of the JSON.
        const int Limit = 1024;
        var data = new string('\u4e2d', 1500);

        var draft = WorkflowEventMapper.DescribeUnrecognized(new UnknownEvent(data), maxPayloadLength: Limit);

        draft.Payload!.Length.ShouldBeLessThanOrEqualTo(Limit);

        using var payload = JsonDocument.Parse(draft.Payload!);
        payload.RootElement.GetProperty("data").GetString()!.ShouldEndWith(RunEventWriter.TruncationMarker);
    }

    [Fact]
    public void A_recognized_event_is_not_described_as_unrecognized()
    {
        WorkflowEventMapper.Map(new WorkflowStartedEvent(message: null)).Draft!.Value.CustomType.ShouldBeNull();
    }

    private static string? DataOf(RunEventDraft draft)
    {
        using var payload = JsonDocument.Parse(draft.Payload!);

        return payload.RootElement.GetProperty("data").GetString();
    }

    private sealed class UnknownEvent(object? data = null) : WorkflowEvent(data);

    private sealed class ExecutorProgressEvent(string executorId, object? data) : ExecutorEvent(executorId, data);

    private sealed class SecretCarrier
    {
        public override string ToString() => "TOKEN-1234";
    }
}
