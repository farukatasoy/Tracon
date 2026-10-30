namespace Tracon.Workflows.UnitTests;

/// <summary>
/// Reading a pending request back from an event whose payload the recorder cut
/// at <c>MaxPayloadLength</c>.
/// </summary>
/// <remarks>
/// The end-to-end case lives in <c>WorkflowHumanInTheLoopTests</c>; these pin
/// the reader on payloads the current writer no longer produces — events
/// recorded before the form moved ahead of the prompt.
/// </remarks>
public sealed class WorkflowRequestDescriptorTests
{
    [Fact]
    public void A_payload_cut_before_its_form_still_yields_the_request_and_a_form_from_its_type()
    {
        // The old order: the prompt before the form, so the cut took the form.
        var cut = """{"requestId":"req-1","portId":"approval-port","requestType":"System.String","responseType":"System.Boolean","prompt":"Approve th""" + "…[truncated]";

        var request = WorkflowRequestDescriptor.Parse(Event(text: "req-1", payload: cut)).ShouldNotBeNull();

        request.RequestId.ShouldBe("req-1");
        request.PortId.ShouldBe("approval-port");
        request.Form.ShouldBe(WorkflowRequestForm.Boolean);
        request.Prompt.ShouldBe("Approve th…[truncated]");
    }

    [Fact]
    public void A_cut_that_splits_an_escape_drops_only_the_split_escape()
    {
        var cut = """{"requestId":"req-1","form":"Text","prompt":"\u015f\u015""" + "…[truncated]";

        var request = WorkflowRequestDescriptor.Parse(Event(text: "req-1", payload: cut)).ShouldNotBeNull();

        request.Form.ShouldBe(WorkflowRequestForm.Text);
        request.Prompt.ShouldBe("\u015F…[truncated]");
    }

    [Fact]
    public void The_request_id_comes_from_the_event_text_when_the_payload_is_unreadable()
    {
        var request = WorkflowRequestDescriptor.Parse(Event(text: "req-1", payload: "{\"req")).ShouldNotBeNull();

        request.RequestId.ShouldBe("req-1");
        request.Prompt.ShouldBeNull();
    }

    [Fact]
    public void An_event_with_no_request_id_anywhere_is_not_a_request()
        => WorkflowRequestDescriptor.Parse(Event(text: null, payload: "{\"portId\":\"p\"}")).ShouldBeNull();

    [Fact]
    public void A_tool_approval_prompt_cut_inside_a_surrogate_pair_keeps_no_half_of_it()
    {
        // The presenter's sentence is cut at 500 characters. Character 500 is the high half
        // of a pair here; the cut kept it alone, and the request - stored as JSON - could not be
        // written.
        var presentation = new ToolApprovalPresentation { Message = new string('a', 499) + "\U0001F600" + "tail" };

        var prompt = WorkflowRequestDescriptor.ToolApprovalPrompt("lookup", presentation);

        prompt.ShouldBe("lookup: " + new string('a', 499) + "…");
        System.Text.Json.JsonSerializer.Serialize(prompt).ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void A_tool_approval_prompt_that_fits_is_not_cut()
    {
        // 498 characters and a pair: exactly the 500-character limit.
        var presentation = new ToolApprovalPresentation { Message = new string('a', 498) + "\U0001F600" };

        WorkflowRequestDescriptor.ToolApprovalPrompt("lookup", presentation)
            .ShouldBe("lookup: " + new string('a', 498) + "\U0001F600");
    }

    private static RunEvent Event(string? text, string payload)
        => new()
        {
            RunId = Guid.NewGuid(),
            Sequence = 1,
            Type = RunEventType.WorkflowRequest,
            Timestamp = DateTimeOffset.UtcNow,
            Text = text,
            Payload = payload,
        };
}
