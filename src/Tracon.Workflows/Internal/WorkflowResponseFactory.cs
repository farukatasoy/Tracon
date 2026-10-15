using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Converts the answer a user gave into an <see cref="ExternalResponse"/>
/// object of the type the port expects.
/// </summary>
/// <remarks>
/// <para>
/// The conversion is done <strong>based on the response type</strong>, and an
/// answer that cannot be converted is rejected with
/// <see cref="TraconException"/>. Silently accepting a wrongly typed
/// answer would break execution at a point the user cannot understand - as a
/// conversion error inside an executor.
/// </para>
/// <para>
/// The response object is built <em>from the request itself</em>
/// (<c>ExternalRequest.CreateResponse</c>). This way the port info and request
/// id are not carried by hand, and the two cannot drift apart.
/// </para>
/// <para>
/// <strong>One rule, two readers.</strong> <see cref="Create"/> reads the
/// request the resumed run republishes, and a refusal there fails the run it
/// opened. <see cref="Check"/> reads the request as the waiting run recorded it,
/// so the HTTP layer refuses a response of the wrong shape with <c>400</c> before
/// any run opens (<see cref="IWorkflowRunner.ValidateResponseAsync"/>). The
/// recorded form is <see cref="WorkflowRequestDescriptor.Form"/> of the same
/// request, so the two take the same branch, and both build their refusals from
/// the helpers below.
/// </para>
/// </remarks>
internal static class WorkflowResponseFactory
{
    /// <summary>Builds the response.</summary>
    /// <param name="request">The request republished from the checkpoint.</param>
    /// <param name="answer">The answer given by the user.</param>
    /// <returns>The response to send to execution.</returns>
    /// <exception cref="TraconException">The answer cannot be converted to the type the port expects.</exception>
    public static ExternalResponse Create(ExternalRequest request, WorkflowAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(answer);

        var portId = request.PortInfo.PortId;

        // Plan approval produces its own response type: MagenticPlanReviewResponse
        // only carries a ChatMessage list, and building it by hand would mean
        // repeating MAF's internal format.
        if (WorkflowRequestDescriptor.IsPlanReview(request) && request.TryGetDataAs<MagenticPlanReviewRequest>(out var review))
        {
            if (answer.Approved is not false)
            {
                return request.CreateResponse(review.Approve());
            }

            return request.CreateResponse(review.Revise(
                answer.Text is { Length: > 0 } revision ? revision : throw new TraconException(RevisionMissing())));
        }

        // Requests answered with a message (including a declarative workflow's
        // user input) carry their own envelope.
        if (request.TryGetDataAs<IExternalRequestEnvelope>(out var envelope))
        {
            var text = answer.Text is { Length: > 0 } given ? given : throw new TraconException(TextMissing(portId));

            return request.CreateResponse(envelope.CreateResponse([new ChatMessage(ChatRole.User, text)]));
        }

        var responseType = request.PortInfo.ResponseType;

        if (responseType.IsMatch<bool>())
        {
            return request.CreateResponse(answer.Approved ?? throw new TraconException(ApprovedMissing(portId)));
        }

        if (responseType.IsMatch<string>())
        {
            return request.CreateResponse(answer.Text is { Length: > 0 } text ? text : throw new TraconException(TextMissing(portId)));
        }

        var target = Resolve(responseType) ?? throw new TraconException(
            $"Response type '{responseType.TypeName}' could not be resolved " +
            "in this process. The assembly defining the type is not loaded; make sure the " +
            "package defining the workflow is referenced by the application.");

        var (value, problem) = Deserialize(answer.Json, target, responseType.TypeName, portId);

        return request.CreateResponse(value ?? throw new TraconException(problem!));
    }

    /// <summary>
    /// Checks an answer against a request as the waiting run recorded it, without
    /// starting a run.
    /// </summary>
    /// <param name="recorded">The recorded request.</param>
    /// <param name="answer">The answer given by the user.</param>
    /// <returns>
    /// The refusal <see cref="Create"/> would raise for this answer; <see langword="null"/>
    /// when the answer fits, or when the record is too old to tell (no form, or for a
    /// <c>data</c> answer no assembly name) - the republished request then decides.
    /// </returns>
    /// <remarks>
    /// A response type this process cannot resolve is not refused here: that is not the
    /// answer's fault, and <see cref="Create"/> reports it on the run.
    /// </remarks>
    public static string? Check(RecordedRequest recorded, WorkflowAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        ArgumentNullException.ThrowIfNull(answer);

        var portId = recorded.PortId;

        switch (recorded.Form)
        {
            case WorkflowRequestForm.PlanReview:
                return answer.Approved is false && answer.Text is not { Length: > 0 } ? RevisionMissing() : null;

            case WorkflowRequestForm.Text:
                return answer.Text is { Length: > 0 } ? null : TextMissing(portId);

            case WorkflowRequestForm.Boolean:
                return answer.Approved is null ? ApprovedMissing(portId) : null;

            case WorkflowRequestForm.Json:
                if (recorded.ResponseType is not { } responseType)
                {
                    return answer.Json is { Length: > 0 } ? null : DataMissing(portId, typeName: null);
                }

                return Resolve(responseType) is { } target
                    ? Deserialize(answer.Json, target, responseType.TypeName, portId).Problem
                    : answer.Json is { Length: > 0 } ? null : DataMissing(portId, responseType.TypeName);

            default:
                return null;
        }
    }

    /// <summary>
    /// Resolves a port's response type in this process. <see cref="TypeId.ToString"/>
    /// produces "Name, AssemblyName, Version=..."; <see cref="Type.GetType(string, bool)"/>
    /// expects exactly this format.
    /// </summary>
    private static Type? Resolve(TypeId responseType)
        => Type.GetType(responseType.ToString(), throwOnError: false);

    /// <summary>Reads a <c>data</c> answer into the port's response type.</summary>
    /// <returns>The value, or the refusal when it is missing or does not read.</returns>
    private static (object? Value, string? Problem) Deserialize(string? json, Type target, string typeName, string portId)
    {
        if (json is not { Length: > 0 })
        {
            return (null, DataMissing(portId, typeName));
        }

        try
        {
            return JsonSerializer.Deserialize(json, target, JsonSerializerOptions.Web) is { } value
                ? (value, null)
                : (null, $"The response body was empty after conversion to type '{typeName}'.");
        }
        catch (JsonException exception)
        {
            return (null, $"The response body could not be converted to type '{typeName}': {exception.Message}");
        }
    }

    private static string RevisionMissing()
        => "The plan was rejected but no revision text was given. The 'text' field is " +
           "required so the manager agent knows what to rebuild the plan against.";

    private static string TextMissing(string portId) => Missing(portId, "a 'text' field");

    private static string ApprovedMissing(string portId) => Missing(portId, "an 'approved' field (yes/no)");

    private static string DataMissing(string portId, string? typeName)
        => Missing(portId, typeName is null ? "a JSON value in the 'data' field" : $"a JSON value of type '{typeName}' in the 'data' field");

    private static string Missing(string portId, string what)
        => $"Port '{portId}' expects {what}, but the response does not have this value.";
}

/// <summary>The raw answer a user gives to a pending request.</summary>
/// <param name="RequestId">The id of the request being answered.</param>
/// <param name="Approved">The yes/no answer.</param>
/// <param name="Text">The text answer.</param>
/// <param name="Json">The free-form JSON answer.</param>
internal sealed record WorkflowAnswer(string RequestId, bool? Approved, string? Text, string? Json);
