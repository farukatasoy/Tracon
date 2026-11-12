using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Marks an answer a fallback link gave, so that the detector outside the chain does not decide on it a second time.
/// </summary>
/// <remarks>
/// <para>
/// A fallback link is built through the same pipeline as the primary, so it carries a
/// <see cref="FinishReasonDetectingChatClient"/> of its own, named for the link's provider. That detector has made the
/// decision for an answer the link gave (a content filter, an output cut off) by the time the answer leaves the link. The
/// detector outside the chain sees the same answer again, and decided again as the primary: a second notice that named
/// the model that had failed.
/// </para>
/// <para>
/// <see cref="FallbackChatClient"/> marks the response (or each update of the stream) of any link after the first; the
/// outer detector takes the mark off and leaves the answer alone. The mark lives in
/// <see cref="ChatResponse.AdditionalProperties"/> for the one hop between the two, and is never seen above the detector.
/// </para>
/// </remarks>
internal static class AnsweredByFallbackLink
{
    private const string Key = "tracon.internal.answered_by_fallback_link";

    /// <summary>Marks a response a link gave.</summary>
    /// <param name="response">The response.</param>
    public static void Mark(ChatResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        (response.AdditionalProperties ??= [])[Key] = true;
    }

    /// <summary>Marks an update of a stream a link gave.</summary>
    /// <param name="update">The update.</param>
    public static void Mark(ChatResponseUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        (update.AdditionalProperties ??= [])[Key] = true;
    }

    /// <summary>Takes the mark off a response.</summary>
    /// <param name="response">The response.</param>
    /// <returns><see langword="true"/> when a link gave the response.</returns>
    public static bool Take(ChatResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return response.AdditionalProperties is { } properties && properties.Remove(Key);
    }

    /// <summary>Takes the mark off an update.</summary>
    /// <param name="update">The update.</param>
    /// <returns><see langword="true"/> when a link gave the update.</returns>
    public static bool Take(ChatResponseUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        return update.AdditionalProperties is { } properties && properties.Remove(Key);
    }
}
