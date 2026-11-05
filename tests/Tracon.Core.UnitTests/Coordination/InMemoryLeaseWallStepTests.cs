namespace Tracon.Core.UnitTests.Coordination;

/// <summary>What the in-memory lease stores do when the wall clock steps while a lease is held.</summary>
/// <remarks>The scenarios live in <see cref="LeaseWallStepContract"/>, which the SQLite project runs too.</remarks>
public sealed class InMemoryLeaseWallStepTests : LeaseWallStepContract
{
    /// <inheritdoc />
    protected override ValueTask<(IJobStore Jobs, ISingletonLeaseStore Leases)> OpenAsync(TimeProvider clock)
        => new(((IJobStore)new InMemoryJobStore(clock), (ISingletonLeaseStore)new InMemorySingletonLeaseStore(clock)));
}
