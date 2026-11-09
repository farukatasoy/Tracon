using Tracon.Tests.Common;

namespace Tracon.Core.UnitTests.TestInfrastructure;

/// <summary>
/// <c>ManualClock.AdvanceUntilAsync</c> carries work that waits more than once to its end without waiting.
/// </summary>
/// <remarks>
/// The reason it exists: work that retries with backoff arms its next timer only after the previous one fired, so one
/// <c>Advance</c> cannot reach the end of it.
/// </remarks>
public sealed class ManualClockTests
{
    [Fact]
    public async Task Work_that_waits_again_after_each_wake_is_carried_to_its_end()
    {
        var clock = new ManualClock();
        var work = WaitThreeTimesAsync(clock);

        await clock.AdvanceUntilAsync(work);

        clock.Steps.ShouldBe([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)]);
    }

    [Fact]
    public async Task A_single_advance_fires_only_the_timer_that_is_armed()
    {
        var clock = new ManualClock();
        var work = WaitThreeTimesAsync(clock);

        clock.Advance(TimeSpan.FromHours(1));

        work.IsCompleted.ShouldBeFalse("the second wait was armed after the first fired, so the one advance never reached it");

        await clock.AdvanceUntilAsync(work);
    }

    [Fact]
    public async Task A_timer_beyond_the_reach_is_left_alone()
    {
        var clock = new ManualClock();
        var deadlineFired = false;

        using var deadline = clock.CreateTimer(_ => deadlineFired = true, null, TimeSpan.FromMinutes(5), Timeout.InfiniteTimeSpan);
        var work = Task.Delay(TimeSpan.FromSeconds(1), clock);

        await clock.AdvanceUntilAsync(work, within: TimeSpan.FromSeconds(30));

        deadlineFired.ShouldBeFalse("a deadline of the system under test is not the wait the call stands in for");
        clock.Steps.ShouldBe([TimeSpan.FromSeconds(1)]);
    }

    [Fact]
    public async Task What_the_work_throws_is_thrown_to_the_caller()
    {
        var clock = new ManualClock();
        var work = FailAfterAsync(clock);

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => clock.AdvanceUntilAsync(work));

        thrown.Message.ShouldBe("the wait ended and the work failed");
        clock.Steps.ShouldBe([TimeSpan.FromSeconds(3)]);
    }

    [Fact]
    public async Task Work_that_never_arms_a_timer_ends_by_the_failure_bound()
    {
        var clock = new ManualClock();
        var never = new TaskCompletionSource();

        await Should.ThrowAsync<TimeoutException>(
            () => clock.AdvanceUntilAsync(never.Task, timeout: TimeSpan.FromMilliseconds(200)));

        clock.Steps.ShouldBeEmpty();
    }

    private static async Task WaitThreeTimesAsync(ManualClock clock)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), clock);
        await Task.Delay(TimeSpan.FromSeconds(2), clock);
        await Task.Delay(TimeSpan.FromSeconds(4), clock);
    }

    private static async Task FailAfterAsync(ManualClock clock)
    {
        await Task.Delay(TimeSpan.FromSeconds(3), clock);

        throw new InvalidOperationException("the wait ended and the work failed");
    }
}
