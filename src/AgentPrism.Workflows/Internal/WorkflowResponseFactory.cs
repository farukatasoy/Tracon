using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Converts the answer a user gave into an <see cref="ExternalResponse"/>
/// object of the type the port expects.
/// </summary>
/// <remarks>
/// <para>
/// The conversion is done <strong>based on the response type</strong>, and an
/// answer that cannot be converted is rejected with
/// <see cref="AgentPrismException"/>. Silently accepting a wrongly typed
/// answer would break execution at a point the user cannot understand - as a
/// conversion error inside an executor.
/// </para>
/// <para>
/// The response object is built <em>from the request itself</em>
/// (<c>ExternalRequest.CreateResponse</c>). This way the port info and request
/// id are not carried by hand, and the two cannot drift apart.
/// </para>
/// </remarks>
internal static class WorkflowResponseFactory
{
    /// <summary>Builds the response.</summary>
    /// <param name="request">The request republished from the checkpoint.</param>
    /// <param name="answer">The answer given by the user.</param>
    /// <returns>The response to send to execution.</returns>
    /// <exception cref="AgentPrismException">The answer cannot be converted to the type the port expects.</exception>
    public static ExternalResponse Create(ExternalRequest request, WorkflowAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(answer);

        // Plan approval produces its own response type: MagenticPlanReviewResponse
        // only carries a ChatMessage list, and building it by hand would mean
        // repeating MAF's internal format.
        if (request.TryGetDataAs<MagenticPlanReviewRequest>(out var review))
        {
            if (answer.Approved is not false)
            {
                return request.CreateResponse(review.Approve());
            }

            if (answer.Text is not { Length: > 0 } revision)
            {
                throw new AgentPrismException(
                    "The plan was rejected but no revision text was given. The 'text' field is " +
                    "required so the manager agent knows what to rebuild the plan against.");
            }

            return request.CreateResponse(review.Revise(revision));
        }

        // Requests answered with a message (including a declarative workflow's
        // user input) carry their own envelope.
        if (request.TryGetDataAs<IExternalRequestEnvelope>(out var envelope))
        {
            var text = Require(answer.Text, request, "text");

            return request.CreateResponse(envelope.CreateResponse([new ChatMessage(ChatRole.User, text)]));
        }

        var responseType = request.PortInfo.ResponseType;

        if (responseType.IsMatch<bool>())
        {
            return request.CreateResponse(
                answer.Approved ?? throw Missing(request, "an 'approved' field (yes/no)"));
        }

        if (responseType.IsMatch<string>())
        {
            return request.CreateResponse(Require(answer.Text, request, "text"));
        }

        return request.CreateResponse(Deserialize(request, answer));
    }

    private static object Deserialize(ExternalRequest request, WorkflowAnswer answer)
    {
        if (answer.Json is not { Length: > 0 } json)
        {
            throw Missing(request, $"a 'json' body of type '{request.PortInfo.ResponseType.TypeName}'");
        }

        // TypeId.ToString() produces "Name, AssemblyName, Version=..."; Type.GetType
        // expects exactly this format.
        var target = Type.GetType(request.PortInfo.ResponseType.ToString(), throwOnError: false)
                     ?? throw new AgentPrismException(
                         $"Response type '{request.PortInfo.ResponseType.TypeName}' could not be resolved " +
                         "in this process. The assembly defining the type is not loaded; make sure the " +
                         "package defining the workflow is referenced by the application.");

        try
        {
            return JsonSerializer.Deserialize(json, target, JsonSerializerOptions.Web)
                   ?? throw new AgentPrismException(
                       $"The response body was empty after conversion to type '{request.PortInfo.ResponseType.TypeName}'.");
        }
        catch (JsonException exception)
        {
            throw new AgentPrismException(
                $"The response body could not be converted to type '{request.PortInfo.ResponseType.TypeName}': {exception.Message}",
                exception);
        }
    }

    private static string Require(string? value, ExternalRequest request, string what)
        => value is { Length: > 0 } text ? text : throw Missing(request, what);

    private static AgentPrismException Missing(ExternalRequest request, string what)
        => new($"Port '{request.PortInfo.PortId}' expects {what}, but the response does not have this value.");
}

/// <summary>The raw answer a user gives to a pending request.</summary>
/// <param name="RequestId">The id of the request being answered.</param>
/// <param name="Approved">The yes/no answer.</param>
/// <param name="Text">The text answer.</param>
/// <param name="Json">The free-form JSON answer.</param>
internal sealed record WorkflowAnswer(string RequestId, bool? Approved, string? Text, string? Json);
