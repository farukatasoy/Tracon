using System.Text.Json;

namespace Tracon;

/// <summary>
/// Recognizes a provider answer that arrived and could not be read: an empty body, a body cut short, a page that is
/// not JSON.
/// </summary>
/// <remarks>
/// <para>
/// The SDK parses a <c>200</c> body itself, and a body it cannot parse reaches the caller as a raw JSON reader failure.
/// That failure carries no HTTP status (the answer was a success) and it is not one of the transport types the retry
/// rules name, so it read as a failure no later try could change. It is the opposite: the call reached the provider,
/// the answer was lost or garbled on the way back, and the next try usually gets a good one.
/// </para>
/// <para>
/// The type alone cannot tell this failure from a JSON failure Tracon or a tool raised, so the decision is made where
/// the provenance is known. <see cref="ProviderFailureTaggingChatClient"/> sits directly above the provider client,
/// below the tool loop: an exception that passes through it came out of the provider client, and a tool never does. It
/// marks the exception, and the classifier reads the mark from the graph. A JSON failure that happened while WRITING a
/// request has no line number (only a failure of the reader sets one), so the check separates the two directions.
/// </para>
/// </remarks>
internal static class UnreadableProviderResponse
{
    private const string MarkKey = "Tracon.UnreadableProviderResponse";

    /// <summary>Marks <paramref name="exception"/> when the provider client failed to read the provider's answer.</summary>
    /// <param name="exception">An exception the provider client threw.</param>
    internal static void MarkWhenUnreadable(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (!FailedToReadAnswer(exception))
        {
            return;
        }

        try
        {
            exception.Data[MarkKey] = true;
        }
        catch (NotSupportedException)
        {
            // An exception with a read-only data bag keeps its old classification; the failure still surfaces.
        }
    }

    /// <summary>Determines whether <paramref name="frame"/> was marked as a failure to read the provider's answer.</summary>
    /// <param name="frame">One exception of a failure graph.</param>
    /// <returns><see langword="true"/> when the provider client's ring marked it.</returns>
    internal static bool IsMarked(Exception frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        return frame.Data.Contains(MarkKey);
    }

    private static bool FailedToReadAnswer(Exception exception)
    {
        foreach (var frame in ProviderFailureGraph.Frames(exception))
        {
            if (frame is JsonException { LineNumber: not null })
            {
                return true;
            }
        }

        return false;
    }
}
