namespace Tracon;

/// <summary>The state of a voice connection.</summary>
internal enum VoiceConversationState
{
    /// <summary><c>start</c> has not arrived yet.</summary>
    New,

    /// <summary>The microphone is open; audio chunks are accumulating.</summary>
    Listening,

    /// <summary>The accumulated audio is being transcribed to text.</summary>
    Transcribing,

    /// <summary>The agent is running and/or the response is being spoken.</summary>
    Responding,

    /// <summary>The connection is closed.</summary>
    Closed,
}

/// <summary>The outcome of a protocol event.</summary>
internal enum VoiceTransitionOutcome
{
    /// <summary>The event was accepted and the state changed.</summary>
    Accepted,

    /// <summary>
    /// The event was meaningless in this state and was silently discarded.
    /// </summary>
    /// <remarks>
    /// Ignoring is <strong>not an error</strong>: a late frame in a race
    /// condition (for example, a <c>cancel</c> that arrives after the turn
    /// has ended) is normal, and showing the client an error would only produce noise.
    /// </remarks>
    Ignored,

    /// <summary>The event violated the protocol; an error is reported to the client.</summary>
    Rejected,
}

/// <summary>
/// The pure state machine of the conversation protocol.
/// </summary>
/// <remarks>
/// <para>
/// There is no network, audio, or agent here — only the question "which
/// event is valid in which state." This lets the protocol be verified with a
/// unit test, without setting up a WebSocket.
/// </para>
/// <para>
/// The class is <strong>concurrent</strong> and carries no lock of its
/// own. Events come from two sources (the receive loop and the turn task),
/// so serializing access is <see cref="VoiceConversationDriver"/>'s responsibility.
/// </para>
/// </remarks>
internal sealed class VoiceConversationStateMachine
{
    /// <summary>The current state.</summary>
    public VoiceConversationState State { get; private set; } = VoiceConversationState.New;

    /// <summary>The number of completed turns.</summary>
    public int Turns { get; private set; }

    /// <summary>Whether the connection is closed.</summary>
    public bool IsClosed => State == VoiceConversationState.Closed;

    /// <summary>Whether an audio chunk is accepted.</summary>
    public bool IsListening => State == VoiceConversationState.Listening;

    /// <summary>Whether a task processing a turn is running.</summary>
    public bool IsBusy => State is VoiceConversationState.Transcribing or VoiceConversationState.Responding;

    /// <summary>Processes the <c>start</c> event.</summary>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// A second <c>start</c> would mean changing the agent; the agent,
    /// session, and tenant are <strong>fixed</strong> for the life of the
    /// connection (29.3).
    /// </remarks>
    public VoiceTransitionOutcome Start() => State == VoiceConversationState.New
        ? Move(VoiceConversationState.Listening)
        : VoiceTransitionOutcome.Rejected;

    /// <summary>Processes a binary audio frame.</summary>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// Audio arriving while the agent is speaking is <strong>ignored</strong>.
    /// An interruption is requested with an explicit <c>cancel</c> message;
    /// a server that treats audio itself as an interruption could mistake
    /// its own speaker output for the user's voice.
    /// </remarks>
    public VoiceTransitionOutcome Audio() => State switch
    {
        VoiceConversationState.Listening => VoiceTransitionOutcome.Accepted,
        VoiceConversationState.Transcribing or VoiceConversationState.Responding => VoiceTransitionOutcome.Ignored,
        _ => VoiceTransitionOutcome.Rejected,
    };

    /// <summary>Processes the <c>commit</c> event.</summary>
    /// <returns>The outcome.</returns>
    public VoiceTransitionOutcome Commit() => State == VoiceConversationState.Listening
        ? Move(VoiceConversationState.Transcribing)
        : VoiceTransitionOutcome.Rejected;

    /// <summary>Transcription finished and the agent is starting to run.</summary>
    /// <returns>The outcome.</returns>
    public VoiceTransitionOutcome BeginResponse() => State == VoiceConversationState.Transcribing
        ? Move(VoiceConversationState.Responding)
        : VoiceTransitionOutcome.Rejected;

    /// <summary>Processes the <c>cancel</c> event.</summary>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// <para>
    /// A <c>cancel</c> that arrives while listening discards the accumulated
    /// audio and continues listening; this is the user saying "I misspoke."
    /// A <c>cancel</c> that arrives while busy interrupts the turn.
    /// </para>
    /// <para>
    /// An interruption <strong>does not change</strong> the state. Only
    /// <see cref="FinishTurn"/> returns it to listening; otherwise the client
    /// could immediately send a new <c>commit</c>, and two turns would run at once.
    /// </para>
    /// </remarks>
    public VoiceTransitionOutcome Cancel() => State switch
    {
        VoiceConversationState.Listening
            or VoiceConversationState.Transcribing
            or VoiceConversationState.Responding => VoiceTransitionOutcome.Accepted,
        _ => VoiceTransitionOutcome.Ignored,
    };

    /// <summary>A turn completed (even if it was interrupted).</summary>
    /// <param name="counted">Whether the turn counter should increment.</param>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// An interrupted turn is still a turn and is counted: the model started
    /// producing a response, and the user heard it.
    /// </remarks>
    public VoiceTransitionOutcome FinishTurn(bool counted)
    {
        if (State == VoiceConversationState.Closed)
        {
            return VoiceTransitionOutcome.Ignored;
        }

        if (counted)
        {
            Turns++;
        }

        return Move(VoiceConversationState.Listening);
    }

    /// <summary>Processes the <c>stop</c> event, or the socket closing.</summary>
    /// <returns>The outcome.</returns>
    public VoiceTransitionOutcome Stop()
        => State == VoiceConversationState.Closed
            ? VoiceTransitionOutcome.Ignored
            : Move(VoiceConversationState.Closed);

    private VoiceTransitionOutcome Move(VoiceConversationState next)
    {
        State = next;
        return VoiceTransitionOutcome.Accepted;
    }
}
