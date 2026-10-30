using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Lets a citation be written once for the message it belongs to, however many updates of a stream
/// show it.
/// </summary>
/// <remarks>
/// <para>
/// A provider adapter can send the same annotation again: when it closes a content part, or when it
/// repeats a part it already streamed. The unit is the message, the way
/// <c>ChatResponseUpdate.ToChatResponse</c> groups updates: a new message starts when the update
/// names another message identifier, or another role. Two messages of one run (two turns of the
/// tool loop) can cite the same source at the same place, and each of them is a citation of its own.
/// </para>
/// <para>
/// The key is where the citation points (address or file), the tool that made it, the cited span and
/// the cited text. The text is in the key because two citations of one page, of different passages
/// of it, differ only there. Not thread-safe: one instance belongs to the one loop that reads a
/// response, in order.
/// </para>
/// </remarks>
internal sealed class CitationTally
{
    private readonly HashSet<CitationKey> _seen = [];
    private string? _messageId;
    private ChatRole? _role;

    /// <summary>Claims the right to write a citation, once for the message it is in.</summary>
    /// <param name="messageId">The identifier of the message the content arrived in, or <see langword="null"/>.</param>
    /// <param name="role">The role of that message, or <see langword="null"/>.</param>
    /// <param name="citation">The citation.</param>
    /// <returns><see langword="true"/> for the first claim of the citation in its message.</returns>
    public bool TryClaim(string? messageId, ChatRole? role, CitationAnnotation citation)
    {
        if (StartsNewMessage(messageId, role))
        {
            _seen.Clear();
        }

        _messageId = messageId ?? _messageId;
        _role = role ?? _role;

        var (start, end) = CitationEventPayload.SpanOf(citation);

        return _seen.Add(new CitationKey(
            citation.Url?.OriginalString ?? citation.FileId,
            citation.ToolName,
            start,
            end,
            citation.Snippet));
    }

    private bool StartsNewMessage(string? messageId, ChatRole? role)
        => (messageId is not null && !string.Equals(messageId, _messageId, StringComparison.Ordinal)) ||
           (role is { } next && _role is { } current && next != current);

    private readonly record struct CitationKey(string? Target, string? ToolName, int? Start, int? End, string? Snippet);
}
