using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Diagnostics;

/// <summary>
/// <see cref="TaskRace"/> names the first of the work, the cutoff and the caller, and leaves no timer or registration
/// running behind it.
/// </summary>
public sealed class TaskRaceTests
{
    [Fact]
    public async Task Work_that_finishes_first_wins()
    {
        var work = Task.CompletedTask;

        (await TaskRace.FinishedFirstAsync(work, TimeSpan.FromMinutes(5), TimeProvider.System, CancellationToken.None))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task A_cutoff_that_elapses_first_wins()
    {
        var never = new TaskCompletionSource();

        (await TaskRace.FinishedFirstAsync(never.Task, TimeSpan.FromMilliseconds(30), TimeProvider.System, CancellationToken.None))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task A_caller_that_cancels_first_wins_and_the_cutoff_is_not_taken_for_it()
    {
        var never = new TaskCompletionSource();
        using var caller = new CancellationTokenSource();

        var racing = TaskRace.FinishedFirstAsync(never.Task, TimeSpan.FromMinutes(5), TimeProvider.System, caller.Token);

        await caller.CancelAsync();

        (await racing).ShouldBeFalse();
        caller.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public async Task Races_that_work_wins_leave_no_timer_running()
    {
        var clock = new ManualClock();

        for (var round = 0; round < 100; round++)
        {
            (await TaskRace.FinishedFirstAsync(Task.CompletedTask, TimeSpan.FromMinutes(5), clock, CancellationToken.None))
                .ShouldBeTrue();
        }

        clock.CreatedTimers.ShouldBe(100);
        clock.RunningTimers.ShouldBe(0, "each race left its cutoff timer scheduled after the work had won");
    }

    [Fact]
    public async Task Races_that_work_wins_leave_no_registration_on_the_callers_token()
    {
        using var caller = new CancellationTokenSource();
        var cancelled = 0;
        using var probe = caller.Token.Register(() => Interlocked.Increment(ref cancelled));

        for (var round = 0; round < 100; round++)
        {
            (await TaskRace.FinishedFirstAsync(Task.CompletedTask, TimeSpan.FromMinutes(5), TimeProvider.System, caller.Token))
                .ShouldBeTrue();
        }

        // Cancelling now must reach only the probe: a leftover race would run its own callback too, and the
        // delay it holds would complete as cancelled. The linked sources were disposed with their races, so
        // cancelling the caller touches nothing else.
        await caller.CancelAsync();

        cancelled.ShouldBe(1);
    }
}
