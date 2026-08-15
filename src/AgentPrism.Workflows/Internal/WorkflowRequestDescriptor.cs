using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Converts a Microsoft Agent Framework external request object into a
/// summary that can be written to and read back from the event stream.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No separate table was opened</strong> for pending requests. A
/// request lives in the payload of a <see cref="RunEventType.WorkflowRequest"/>
/// event; the event stream is already append-only, tenant-filtered, and
/// pageable. A second recording path would keep the same information in two
/// places and let them drift apart over time.
/// </para>
/// <para>
/// The summary is enough to <em>display</em> the request, not to
/// <em>reconstruct</em> it. When answering, the real <see cref="ExternalRequest"/>
/// object is republished by the execution resumed from the checkpoint and
/// matched by id - measured (phase 16): resuming reissues the same request
/// with the same <c>RequestId</c>.
/// </para>
/// </remarks>
internal static class WorkflowRequestDescriptor
{
    private static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Converts the request into an event payload.</summary>
    /// <param name="request">The Microsoft Agent Framework request.</param>
    /// <returns>A JSON summary.</returns>
    public static string Describe(ExternalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var snapshot = new Snapshot
        {
            RequestId = request.RequestId,
            PortId = request.PortInfo.PortId,
            RequestType = request.PortInfo.RequestType.TypeName,
            ResponseType = request.PortInfo.ResponseType.TypeName,
            Prompt = Prompt(request),
            Form = Form(request),
        };

        return JsonSerializer.Serialize(snapshot, Options);
    }

    /// <summary>
    /// Converts a <see cref="RunEventType.WorkflowRequest"/> event into a pending request.
    /// </summary>
    /// <param name="runEvent">The event.</param>
    /// <returns>The pending request; <see langword="null"/> if the payload cannot be read.</returns>
    /// <remarks>
    /// If the payload cannot be read, the event is <strong>dropped</strong>.
    /// Reason: the payload may have been truncated by the recording settings
    /// (<c>MaxPayloadLength</c>), and fabricating a request from a partial JSON
    /// would show the user a card that cannot be answered.
    /// </remarks>
    public static WorkflowPendingRequest? Parse(RunEvent runEvent)
    {
        ArgumentNullException.ThrowIfNull(runEvent);

        if (runEvent.Payload is not { Length: > 0 } payload)
        {
            return null;
        }

        Snapshot? snapshot;

        try
        {
            snapshot = JsonSerializer.Deserialize<Snapshot>(payload, Options);
        }
        catch (JsonException)
        {
            return null;
        }

        if (snapshot is null || string.IsNullOrEmpty(snapshot.RequestId))
        {
            return null;
        }

        return new WorkflowPendingRequest
        {
            RunId = runEvent.RunId,
            RequestId = snapshot.RequestId,
            PortId = snapshot.PortId ?? string.Empty,
            RequestType = snapshot.RequestType,
            ResponseType = snapshot.ResponseType,
            Prompt = snapshot.Prompt,
            Form = snapshot.Form,
            RequestedAt = runEvent.Timestamp,
        };
    }

    /// <summary>Chooses the input form the UI should ask for, based on the port's response type.</summary>
    /// <param name="request">The Microsoft Agent Framework request.</param>
    /// <returns>The input form.</returns>
    public static WorkflowRequestForm Form(ExternalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.IsDataOfType<MagenticPlanReviewRequest>())
        {
            return WorkflowRequestForm.PlanReview;
        }

        // Every request answered with a message list - including declarative
        // workflows - goes through this UI; a text box is the right question.
        if (request.TryGetDataAs<IExternalRequestEnvelope>(out _))
        {
            return WorkflowRequestForm.Text;
        }

        if (request.PortInfo.ResponseType.IsMatch<bool>())
        {
            return WorkflowRequestForm.Boolean;
        }

        return request.PortInfo.ResponseType.IsMatch<string>()
            ? WorkflowRequestForm.Text
            : WorkflowRequestForm.Json;
    }

    /// <summary>Extracts the request text to show the user.</summary>
    private static string? Prompt(ExternalRequest request)
    {
        if (request.TryGetDataAs<MagenticPlanReviewRequest>(out var review))
        {
            var plan = review.Plan.Text;

            return review.IsStalled
                ? $"Execution stalled and the plan was rebuilt.{Environment.NewLine}{plan}"
                : plan;
        }

        if (request.TryGetDataAs<IExternalRequestEnvelope>(out var envelope))
        {
            return envelope.GetInnerRequestContent() is TextContent text ? text.Text : null;
        }

        if (request.TryGetDataAs<string>(out var value))
        {
            return value;
        }

        if (request.TryGetDataAs<ChatMessage>(out var message))
        {
            return message.Text;
        }

        // Dumping an unknown payload with ToString() would bloat the event
        // table; the UI can still draw a meaningful card with just the type name.
        return request.PortInfo.RequestType.TypeName;
    }

    /// <summary>The wire format of the event payload.</summary>
    private sealed record Snapshot
    {
        public string? RequestId { get; init; }

        public string? PortId { get; init; }

        public string? RequestType { get; init; }

        public string? ResponseType { get; init; }

        public string? Prompt { get; init; }

        public WorkflowRequestForm Form { get; init; }
    }
}
