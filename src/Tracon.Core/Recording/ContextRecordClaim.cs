namespace Tracon;

/// <summary>
/// The right to write a run's one <see cref="RunEventCustomTypes.Context"/> event, claimed by the first
/// model call that asks.
/// </summary>
/// <remarks>
/// <para>
/// A holder object, not a flag on <see cref="AgentRunScope"/>: the scope is a record, and a record's
/// equality compares every field. A flag would make two scopes of one run unequal after one of them
/// was claimed. The scope's copies (a side-channel call runs on <c>scope with { ... }</c>) share this
/// object, which is what makes the claim the run's and not the copy's.
/// </para>
/// <para>
/// The same ambient-write/scoped-read pattern as <see cref="HostedCallLedger"/>: the scope makes the
/// object, the chat ring claims it, and the object lives as long as the run.
/// </para>
/// </remarks>
internal sealed class ContextRecordClaim
{
    private int _claimed;

    /// <summary>Claims the right to write the event, once.</summary>
    /// <returns><see langword="true"/> for the first claim, <see langword="false"/> after it.</returns>
    /// <remarks>Atomic: the model calls of a tool round can start together.</remarks>
    public bool TryClaim() => Interlocked.Exchange(ref _claimed, 1) == 0;
}
