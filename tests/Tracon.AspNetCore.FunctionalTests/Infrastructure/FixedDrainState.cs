namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>A drain state that never changes; registered in place of the host's own.</summary>
/// <param name="isDraining">What <see cref="IsDraining"/> answers.</param>
internal sealed class FixedDrainState(bool isDraining) : ITraconDrainState
{
    /// <inheritdoc />
    public bool IsDraining => isDraining;
}
