namespace Tracon;

/// <summary>
/// Settings of how a conversation kept in a session is written.
/// </summary>
/// <remarks>
/// <para>
/// Read from the <c>Tracon:Sessions</c> configuration section.
/// </para>
/// <para>
/// A turn that is canceled before it finishes leaves nothing in the session's history by default: the framework
/// writes a turn's messages when the turn ends, and a canceled turn does not end. The next turn then answers
/// as if the message had never been sent. <see cref="KeepCanceledTurns"/> changes that for the turns Tracon's own
/// streaming endpoints cancel.
/// </para>
/// </remarks>
public sealed class TraconSessionOptions
{
    /// <summary>The configuration section these settings are read from.</summary>
    public const string SectionName = "Tracon:Sessions";

    /// <summary>
    /// Gets or sets a value indicating whether a streamed turn the client cancels after the model began to answer
    /// is kept in the session's history. Off by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When on, the user's message of the canceled turn is written, with the text that had streamed so far and a note
    /// that the answer was cut off, so the next turn sees what was asked and what the user saw. The history then holds
    /// the user's text for turns that did not complete, and that text is subject to the same retention and erasure as
    /// the rest of the history.
    /// </para>
    /// <para>
    /// A turn counts once the model has started to answer: a cancel before that point writes nothing, because the
    /// message has not been accepted yet (the content guards have not necessarily run). A turn that is not streamed
    /// writes nothing when it is canceled. A host that runs agents from its own code calls
    /// <c>AgentSessionManager.RecordCanceledTurnAsync</c> itself.
    /// </para>
    /// </remarks>
    public bool KeepCanceledTurns { get; set; }
}
