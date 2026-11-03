using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Gives a chunk that carries no message id the id of the message before it, so one streamed answer is one AG-UI message.
/// </summary>
/// <remarks>
/// <para>
/// Microsoft.Extensions.AI reads a chunk with no message id as a continuation of the message before it, unless its role
/// differs. The converter that writes the AG-UI events does not: it opens a text message for every chunk whose id differs
/// from the one before, and a chunk without an id gets one of its own. A provider that streams a word at a time without
/// ids (the example host's echo model, a provider written against the minimum of the abstraction) then produced one
/// assistant message per word, and a front end drew one bubble for each.
/// </para>
/// <para>
/// A chunk that has an id keeps it. Only the missing ones are filled in, and a change of role starts a message of its own,
/// as it does when the chunks are joined into a response.
/// </para>
/// </remarks>
internal sealed class AGUIMessageIdStabilizer
{
    private string? _current;
    private ChatRole? _role;
    private int _generated;

    /// <summary>Fills in the message id of a chunk that has none.</summary>
    /// <param name="update">The chunk; its message id is set when it was missing.</param>
    /// <returns>The same chunk.</returns>
    public ChatResponseUpdate Apply(ChatResponseUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (update.MessageId is { Length: > 0 } given)
        {
            _current = given;
        }
        else
        {
            if (_current is null || (update.Role is { } role && _role is { } previous && role != previous))
            {
                _generated++;
                _current = $"tracon-message-{_generated}";
            }

            update.MessageId = _current;
        }

        _role = update.Role ?? _role;

        return update;
    }
}
