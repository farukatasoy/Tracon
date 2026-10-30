using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// The early check reads a pending request as the waiting run recorded it, and
/// refuses exactly what the conversion of the republished request would refuse.
/// </summary>
/// <remarks>
/// The HTTP layer turns a refusal into <c>400</c> before any run opens
/// (<c>WorkflowRespondValidationTests</c>). This class pins the rule: the recorded event
/// carries what the check needs, and <see cref="WorkflowResponseFactory.Check"/> and
/// <see cref="WorkflowResponseFactory.Create"/> agree word for word.
/// </remarks>
public sealed class WorkflowResponseCheckTests
{
    private const string PortId = "decision-port";

    [Fact]
    public void A_recorded_request_carries_its_form_and_its_full_response_type()
    {
        var recorded = Record(DecisionRequest("Review this"));

        recorded.Form.ShouldBe(WorkflowRequestForm.Json);
        recorded.PortId.ShouldBe(PortId);
        recorded.ResponseType.ShouldBe(new TypeId(typeof(Decision)));
    }

    [Fact]
    public void A_payload_cut_at_the_length_limit_still_carries_what_the_check_needs()
    {
        // The prompt is written last, so a cut costs only the prompt's tail. A non-ASCII
        // character is escaped to six, so 3,000 of them run far past half the payload.
        var request = DecisionRequest(new string('é', 3_000));
        var payload = WorkflowRequestDescriptor.Describe(request);

        var recorded = WorkflowRequestDescriptor.ReadRecorded(new RunEvent
        {
            RunId = TraconId.NewId(),
            Sequence = 1,
            Type = RunEventType.WorkflowRequest,
            Timestamp = DateTimeOffset.UtcNow,
            Text = request.RequestId,
            Payload = payload[..(payload.Length / 2)] + "…[truncated]",
        }).ShouldNotBeNull();

        recorded.Form.ShouldBe(WorkflowRequestForm.Json);
        recorded.ResponseType.ShouldBe(new TypeId(typeof(Decision)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("""{"approved":"maybe"}""")]
    [InlineData("\"yes\"")]
    [InlineData("null")]
    public void A_data_answer_that_does_not_read_into_the_type_is_refused_the_way_the_conversion_refuses_it(string? json)
    {
        var request = DecisionRequest("Review this");
        var answer = Answer(request, json: json);

        var refusal = WorkflowResponseFactory.Check(Record(request), answer);

        refusal.ShouldNotBeNull();
        Should.Throw<TraconException>(() => WorkflowResponseFactory.Create(request, answer)).Message.ShouldBe(refusal);
    }

    [Fact]
    public void A_data_answer_that_reads_into_the_type_passes()
    {
        var request = DecisionRequest("Review this");

        WorkflowResponseFactory.Check(Record(request), Answer(request, json: """{"approved":true,"note":"ok"}""")).ShouldBeNull();
    }

    [Fact]
    public void A_yes_no_port_needs_approved_and_says_so_like_the_conversion()
    {
        var request = ExternalRequest.Create(RequestPort.Create<string, bool>("approval-port"), "Approve?", "req-2");
        var answer = Answer(request, text: "yes");

        var refusal = WorkflowResponseFactory.Check(Record(request), answer);

        refusal.ShouldNotBeNull().ShouldContain("'approved'", Case.Sensitive);
        Should.Throw<TraconException>(() => WorkflowResponseFactory.Create(request, answer)).Message.ShouldBe(refusal);
        WorkflowResponseFactory.Check(Record(request), Answer(request, approved: false)).ShouldBeNull();
    }

    [Fact]
    public void A_text_port_needs_text_and_says_so_like_the_conversion()
    {
        var request = ExternalRequest.Create(RequestPort.Create<string, string>("note-port"), "Comment?", "req-3");
        var answer = Answer(request, approved: true);

        var refusal = WorkflowResponseFactory.Check(Record(request), answer);

        refusal.ShouldNotBeNull().ShouldContain("'text'", Case.Sensitive);
        Should.Throw<TraconException>(() => WorkflowResponseFactory.Create(request, answer)).Message.ShouldBe(refusal);
        WorkflowResponseFactory.Check(Record(request), Answer(request, text: "fine")).ShouldBeNull();
    }

    [Fact]
    public void A_plan_review_refuses_only_a_rejection_without_revision_text()
    {
        var recorded = new RecordedRequest("req-4", "plan-port", WorkflowRequestForm.PlanReview, ResponseType: null);

        WorkflowResponseFactory.Check(recorded, new WorkflowAnswer("req-4", Approved: false, Text: null, Json: null))
            .ShouldNotBeNull().ShouldContain("revision text", Case.Sensitive);
        WorkflowResponseFactory.Check(recorded, new WorkflowAnswer("req-4", Approved: false, Text: "fewer steps", Json: null)).ShouldBeNull();
        WorkflowResponseFactory.Check(recorded, new WorkflowAnswer("req-4", Approved: null, Text: null, Json: null)).ShouldBeNull();
    }

    [Fact]
    public void A_record_without_a_form_is_left_to_the_republished_request()
        => WorkflowResponseFactory.Check(
                new RecordedRequest("req-5", PortId, Form: null, ResponseType: null),
                new WorkflowAnswer("req-5", Approved: null, Text: null, Json: null))
            .ShouldBeNull();

    [Fact]
    public void A_record_without_the_response_assembly_checks_only_that_data_is_there()
    {
        // An event written before the assembly name was recorded.
        var recorded = new RecordedRequest("req-6", PortId, WorkflowRequestForm.Json, ResponseType: null);

        WorkflowResponseFactory.Check(recorded, new WorkflowAnswer("req-6", null, null, Json: null))
            .ShouldNotBeNull().ShouldContain("'data'", Case.Sensitive);
        WorkflowResponseFactory.Check(recorded, new WorkflowAnswer("req-6", null, null, Json: "\"anything\"")).ShouldBeNull();
    }

    [Fact]
    public void A_response_type_this_process_cannot_resolve_is_not_the_answers_fault()
    {
        var recorded = new RecordedRequest(
            "req-7",
            PortId,
            WorkflowRequestForm.Json,
            new TypeId("No.Such.Assembly, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", "No.Such.Type"));

        WorkflowResponseFactory.Check(recorded, new WorkflowAnswer("req-7", null, null, Json: "{}")).ShouldBeNull();
        WorkflowResponseFactory.Check(recorded, new WorkflowAnswer("req-7", null, null, Json: null))
            .ShouldNotBeNull().ShouldContain("No.Such.Type", Case.Sensitive);
    }

    private static ExternalRequest DecisionRequest(string prompt)
        => ExternalRequest.Create(RequestPort.Create<string, Decision>(PortId), prompt, "req-1");

    private static RecordedRequest Record(ExternalRequest request)
        => WorkflowRequestDescriptor.ReadRecorded(new RunEvent
        {
            RunId = TraconId.NewId(),
            Sequence = 1,
            Type = RunEventType.WorkflowRequest,
            Timestamp = DateTimeOffset.UtcNow,
            Text = request.RequestId,
            Payload = WorkflowRequestDescriptor.Describe(request),
        }).ShouldNotBeNull();

    private static WorkflowAnswer Answer(ExternalRequest request, bool? approved = null, string? text = null, string? json = null)
        => new(request.RequestId, approved, text, json);

    /// <summary>The structured answer the decision port expects.</summary>
    internal sealed record Decision(bool Approved, string? Note);
}
