using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace AgentPrism.Workflows.UnitTests;

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
    public void Executor_error_is_mapped()
    {
        var mapping = WorkflowEventMapper.Map(
            new ExecutorFailedEvent("writer", new InvalidOperationException("blew up")));

        mapping.Draft!.Value.Type.ShouldBe(RunEventType.ExecutorFailed);
        mapping.Draft!.Value.Text.ShouldBe("writer");
        mapping.Draft!.Value.Payload.ShouldBe("blew up");
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
    public void Unknown_event_is_marked_as_unrecognized()
    {
        // An event silently dropped would make debugging impossible the day
        // MAF adds a new type.
        var mapping = WorkflowEventMapper.Map(new UnknownEvent());

        mapping.IsKnown.ShouldBeFalse();
        mapping.Draft.ShouldBeNull();
    }

    private sealed class UnknownEvent() : WorkflowEvent(data: null);
}
