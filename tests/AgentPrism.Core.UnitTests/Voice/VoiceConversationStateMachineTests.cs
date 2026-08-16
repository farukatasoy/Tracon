namespace AgentPrism.Core.UnitTests.Voice;

/// <summary>
/// Validates the state machine of the conversation protocol.
/// </summary>
/// <remarks>
/// The state machine is pure: there is no network, audio, or agent. Protocol
/// correctness can therefore be checked without setting up a WebSocket.
/// </remarks>
public sealed class VoiceConversationStateMachineTests
{
    [Fact]
    public void Only_start_is_accepted_initially()
    {
        var machine = new VoiceConversationStateMachine();

        machine.Commit().ShouldBe(VoiceTransitionOutcome.Rejected);
        machine.Audio().ShouldBe(VoiceTransitionOutcome.Rejected);
        machine.Start().ShouldBe(VoiceTransitionOutcome.Accepted);
        machine.State.ShouldBe(VoiceConversationState.Listening);
    }

    [Fact]
    public void Second_start_is_rejected()
    {
        // The agent, session and tenant are fixed for the whole connection
        // (29.3): a second `start` would move authorization to after the
        // handshake.
        var machine = new VoiceConversationStateMachine();

        machine.Start().ShouldBe(VoiceTransitionOutcome.Accepted);
        machine.Start().ShouldBe(VoiceTransitionOutcome.Rejected);
    }

    [Fact]
    public void Full_turn_start_commit_response_end()
    {
        var machine = new VoiceConversationStateMachine();

        machine.Start();
        machine.Audio().ShouldBe(VoiceTransitionOutcome.Accepted);

        machine.Commit().ShouldBe(VoiceTransitionOutcome.Accepted);
        machine.State.ShouldBe(VoiceConversationState.Transcribing);

        machine.BeginResponse().ShouldBe(VoiceTransitionOutcome.Accepted);
        machine.State.ShouldBe(VoiceConversationState.Responding);

        machine.FinishTurn(counted: true).ShouldBe(VoiceTransitionOutcome.Accepted);
        machine.State.ShouldBe(VoiceConversationState.Listening);
        machine.Turns.ShouldBe(1);
    }

    [Fact]
    public void Audio_arriving_while_the_agent_is_speaking_is_ignored()
    {
        // Interruption is requested with an explicit `cancel`. A server that
        // treats audio itself as an interruption could mistake its own
        // speaker output for the user's voice.
        var machine = new VoiceConversationStateMachine();

        machine.Start();
        machine.Commit();
        machine.BeginResponse();

        machine.Audio().ShouldBe(VoiceTransitionOutcome.Ignored);
        machine.State.ShouldBe(VoiceConversationState.Responding);
    }

    [Fact]
    public void Commit_arriving_while_busy_is_rejected()
    {
        var machine = new VoiceConversationStateMachine();

        machine.Start();
        machine.Commit();

        machine.Commit().ShouldBe(VoiceTransitionOutcome.Rejected);
    }

    [Fact]
    public void Interruption_does_not_change_state_only_marks_the_turn()
    {
        // 🚨 If a cancellation moved the state back to Listening, the client
        // could immediately send a new `commit` and two turns would run at
        // once.
        var machine = new VoiceConversationStateMachine();

        machine.Start();
        machine.Commit();
        machine.BeginResponse();

        machine.Cancel().ShouldBe(VoiceTransitionOutcome.Accepted);
        machine.State.ShouldBe(VoiceConversationState.Responding);
        machine.Commit().ShouldBe(VoiceTransitionOutcome.Rejected);

        machine.FinishTurn(counted: true);
        machine.State.ShouldBe(VoiceConversationState.Listening);
        machine.Turns.ShouldBe(1);
    }

    [Fact]
    public void Interruption_while_listening_keeps_listening()
    {
        var machine = new VoiceConversationStateMachine();

        machine.Start();

        machine.Cancel().ShouldBe(VoiceTransitionOutcome.Accepted);
        machine.State.ShouldBe(VoiceConversationState.Listening);
    }

    [Fact]
    public void Empty_turn_does_not_count()
    {
        var machine = new VoiceConversationStateMachine();

        machine.Start();
        machine.Commit();
        machine.FinishTurn(counted: false);

        machine.Turns.ShouldBe(0);
        machine.State.ShouldBe(VoiceConversationState.Listening);
    }

    [Fact]
    public void Stop_closes_from_any_state_and_nothing_after_is_accepted()
    {
        var machine = new VoiceConversationStateMachine();

        machine.Start();
        machine.Commit();

        machine.Stop().ShouldBe(VoiceTransitionOutcome.Accepted);
        machine.IsClosed.ShouldBeTrue();

        machine.Stop().ShouldBe(VoiceTransitionOutcome.Ignored);
        machine.Start().ShouldBe(VoiceTransitionOutcome.Rejected);
        machine.Audio().ShouldBe(VoiceTransitionOutcome.Rejected);
        machine.Commit().ShouldBe(VoiceTransitionOutcome.Rejected);
        machine.FinishTurn(counted: true).ShouldBe(VoiceTransitionOutcome.Ignored);
        machine.Turns.ShouldBe(0);
    }
}
