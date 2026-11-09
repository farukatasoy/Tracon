namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Moves a fake clock through the wait limits of a sub-agent call, once the child is inside its model call.
/// </summary>
/// <remarks>
/// Both limits (the child deadline and the hard cutoff) count on the clock the host was given, so neither can pass
/// before the test moves it. A real 50 ms deadline once passed while a loaded machine was still starting the child: the
/// child then saw a cancelled token before it reached the model, reported the cooperative layer, and left no run row,
/// so the test failed although the product was right.
/// </remarks>
internal static class ChildLimits
{
    /// <summary>
    /// Waits until the child has started and every limit is armed on the clock, then moves the clock to each limit in turn.
    /// </summary>
    /// <param name="clock">The clock the host was started with, not yet moved by the test.</param>
    /// <param name="childStarted">Completes once the child is inside its model call.</param>
    /// <param name="limits">The wait limits the call was configured with, as the time from the start of the call.</param>
    /// <returns>The task that completes once the clock has passed the last limit.</returns>
    /// <remarks>
    /// The waits observe state: the child has started, the timers are armed. Only <see cref="ManualClock.Advance"/> moves
    /// time.
    /// </remarks>
    public static async Task ExpireAsync(ManualClock clock, Task childStarted, params TimeSpan[] limits)
    {
        await childStarted.WaitAsync(TestContext.Current.CancellationToken);

        await WaitUntil.TrueAsync(
            () => limits.All(limit => clock.Pending.Contains(limit)),
            "the wait limits of the child call to be armed on the host's clock");

        var passed = TimeSpan.Zero;

        foreach (var limit in limits.Order())
        {
            clock.Advance(limit - passed);
            passed = limit;
        }
    }
}
