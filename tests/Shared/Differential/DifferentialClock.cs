using Tracon.Tests.Common;

namespace Tracon.Tests.Differential;

/// <summary>
/// The clock a job sequence moves: the one clock whose reading decides when a lease ends, when a retry delay is over and
/// whether a job is due.
/// </summary>
/// <remarks>
/// Which clock that is depends on the provider. The in-memory store and SQLite read the application's clock, so the
/// sequence moves a <see cref="ManualClock"/> the store was built with. PostgreSQL and SQL Server read the DATABASE
/// SERVER's clock inside the statement, which a test cannot move; their test projects stand a one-row clock in the
/// schema in its place and move that. Either way a lease expires when a step says so, not when a test waits.
/// </remarks>
internal abstract class DifferentialClock
{
    /// <summary>Gets the clock's reading.</summary>
    public abstract DateTimeOffset Now { get; }

    /// <summary>Moves the clock forward.</summary>
    /// <param name="amount">How far.</param>
    /// <returns>The completion task.</returns>
    public abstract ValueTask AdvanceAsync(TimeSpan amount);

    /// <summary>Wraps the manual clock a store was built with.</summary>
    /// <param name="clock">The clock.</param>
    /// <returns>The differential clock.</returns>
    public static DifferentialClock Of(ManualClock clock) => new ManualDifferentialClock(clock);

    private sealed class ManualDifferentialClock(ManualClock clock) : DifferentialClock
    {
        public override DateTimeOffset Now => clock.GetUtcNow();

        public override ValueTask AdvanceAsync(TimeSpan amount)
        {
            clock.Advance(amount);

            return ValueTask.CompletedTask;
        }
    }
}
