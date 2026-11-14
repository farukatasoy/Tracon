using System.Runtime.CompilerServices;
using Google.GenAI.Types;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Refuses an answer that Gemini ended because it could not read the function call the model wrote, and that carries no call.
/// </summary>
/// <remarks>
/// <para>
/// The API ends a turn with finish reason <c>MALFORMED_FUNCTION_CALL</c> (the model wrote a call that did not parse) or
/// <c>UNEXPECTED_TOOL_CALL</c> (it asked for a tool the request did not declare), and the answer holds no function call and
/// often no text. Google.GenAI 1.27.0 maps both to <see cref="ChatFinishReason.ToolCalls"/>, so the tool loop takes the turn for
/// one that has a call to run, and without this client the run would complete with an empty answer: the failure would be
/// recorded as a success. A Gemini 3 model ends turns this way on a request that invites a call without declaring a tool.
/// </para>
/// <para>
/// The failure is raised from the provider's own client, so the model-call boundary records it as any other provider failure
/// (<see cref="RunErrorClass.ProviderError"/>) and a fallback chain can go on to the next model. An answer that does hold a
/// function call is left alone: the call is what the tool loop acts on, and the finish reason is then true.
/// </para>
/// <para>
/// Sits directly on the adapter, because the raw response (<see cref="GenerateContentResponse"/>) is what carries the reason.
/// </para>
/// </remarks>
internal sealed class GoogleFunctionCallChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

        if (FailedReason(response.RawRepresentation) is { } reason
            && !response.Messages.SelectMany(static message => message.Contents).OfType<FunctionCallContent>().Any())
        {
            throw new GoogleFunctionCallException(reason);
        }

        return response;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sawCall = false;

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            sawCall |= update.Contents.OfType<FunctionCallContent>().Any();

            if (!sawCall && FailedReason(update.RawRepresentation) is { } reason)
            {
                throw new GoogleFunctionCallException(reason);
            }

            yield return update;
        }
    }

    /// <summary>The reason a turn failed on a function call, or <see langword="null"/> when it did not.</summary>
    private static string? FailedReason(object? raw)
    {
        if (raw is not GenerateContentResponse { Candidates: { Count: > 0 } candidates } || candidates[0] is not { } candidate)
        {
            return null;
        }

        if (candidate.FinishReason == FinishReason.MalformedFunctionCall)
        {
            return "MALFORMED_FUNCTION_CALL";
        }

        return candidate.FinishReason == FinishReason.UnexpectedToolCall ? "UNEXPECTED_TOOL_CALL" : null;
    }
}

/// <summary>
/// Gemini ended a turn because it could not use the function call the model wrote, and the answer holds no call.
/// </summary>
/// <remarks>
/// Thrown by the provider's own client, so the model-call boundary records it like any other provider failure: error type
/// <c>upstream_error</c>, class <see cref="RunErrorClass.ProviderError"/>. The run does not complete with an empty answer.
/// </remarks>
internal sealed class GoogleFunctionCallException(string reason)
    : Exception(
        $"The Gemini API ended the turn with finish reason '{reason}': the model wrote a function call it could not use, " +
        "so the answer holds no call and no text to act on.");
