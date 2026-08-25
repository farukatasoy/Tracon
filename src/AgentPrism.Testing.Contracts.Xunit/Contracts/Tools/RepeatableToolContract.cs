namespace AgentPrism.Testing.Contracts.Tools;

/// <summary>Behavior tests for a custom tool that can safely be repeated.</summary>
/// <remarks>
/// Derive from this contract only when the tool declares
/// <see cref="AgentPrismToolRegistration.SafeToRepeat"/>. The base
/// <see cref="CustomToolContract"/> remains the contract for tools that do
/// not make that idempotency promise.
/// </remarks>
public abstract class RepeatableToolContract : CustomToolContract
{
    /// <summary>Verifies that the registration makes the repeatability promise.</summary>
    [Fact]
    public void Registration_is_marked_safe_to_repeat()
        => Registration.SafeToRepeat.ShouldBeTrue();
}
