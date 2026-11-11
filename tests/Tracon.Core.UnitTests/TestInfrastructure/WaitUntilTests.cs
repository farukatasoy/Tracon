
namespace Tracon.Core.UnitTests.TestInfrastructure;

/// <summary>
/// The condition wait every test project links (<c>tests/Shared/Waiting/WaitUntil.cs</c>).
/// </summary>
/// <remarks>
/// A broken wait fails far from here: it either hangs a whole test run or
/// turns a real failure green. These cases pin the three ways a wait ends -
/// the condition holds, the bound passes, or the caller cancels - and that an
/// exception from the probe is not swallowed as "not yet". The short bounds
/// below are what the tests assert, not a guess about machine speed.
/// </remarks>
public sealed class WaitUntilTests
{
    [Fact]
    public async Task A_condition_that_already_holds_is_probed_once()
    {
        var probes = 0;

        await WaitUntil.TrueAsync(() => ++probes > 0, "a condition that already holds");

        probes.ShouldBe(1);
    }

    [Fact]
    public async Task The_wait_ends_on_the_first_probe_that_holds()
    {
        var probes = 0;

        await WaitUntil.TrueAsync(() => ++probes == 3, "the third probe");

        probes.ShouldBe(3);
    }

    [Fact]
    public async Task The_value_the_wait_accepted_is_returned()
    {
        var probes = 0;

        var value = await WaitUntil.ValueAsync(
            () => Task.FromResult(++probes),
            static probed => probed >= 2,
            "the second value");

        value.ShouldBe(2);
    }

    [Fact]
    public async Task A_condition_that_never_holds_times_out_instead_of_looping_forever()
    {
        var timeout = await Should.ThrowAsync<TimeoutException>(
            () => WaitUntil.TrueAsync(static () => false, "a condition that never holds", TimeSpan.FromMilliseconds(100)));

        timeout.Message.ShouldContain("a condition that never holds");
    }

    [Fact]
    public async Task The_timeout_says_how_often_it_polled_and_how_far_apart_the_polls_were()
    {
        var timeout = await Should.ThrowAsync<TimeoutException>(
            () => WaitUntil.TrueAsync(static () => false, "a condition that never holds", TimeSpan.FromMilliseconds(100)));

        timeout.Message.ShouldMatch(@"Polls: [1-9][0-9]*; longest gap between polls: [0-9]+ ms");
    }

    [Fact]
    public async Task The_timeout_says_what_the_process_heartbeat_saw_while_it_waited()
    {
        var timeout = await Should.ThrowAsync<TimeoutException>(
            () => WaitUntil.TrueAsync(static () => false, "a condition that never holds", TimeSpan.FromMilliseconds(100)));

        // Either the worst delays inside the wait, or the statement that the heartbeat had nothing inside it: the message
        // never leaves the reader to guess whether the instrument was running.
        timeout.Message.ShouldMatch(
            @"(longest process stall: [0-9]+ ms; longest thread pool delay: [0-9]+ ms|the process heartbeat recorded nothing inside this wait)");
    }

    [Fact]
    public async Task A_stuck_probe_timeout_carries_the_same_diagnosis()
    {
        var never = new TaskCompletionSource<bool>();

        var timeout = await Should.ThrowAsync<TimeoutException>(
            () => WaitUntil.TrueAsync(() => never.Task, "a stuck probe", TimeSpan.FromMilliseconds(100)));

        timeout.Message.ShouldMatch(@"Polls: 1; longest gap between polls: 0 ms");
    }

    [Fact]
    public async Task Without_a_description_the_timeout_quotes_the_condition_itself()
    {
        var ready = false;

        var timeout = await Should.ThrowAsync<TimeoutException>(
            () => WaitUntil.TrueAsync(() => ready, timeout: TimeSpan.FromMilliseconds(100)));

        timeout.Message.ShouldContain("() => ready");
    }

    [Fact]
    public async Task The_timeout_names_the_last_value_it_probed()
    {
        var timeout = await Should.ThrowAsync<TimeoutException>(
            () => WaitUntil.ValueAsync(
                static () => Task.FromResult("Running"),
                static status => string.Equals(status, "Completed", StringComparison.Ordinal),
                "the run to complete",
                TimeSpan.FromMilliseconds(100)));

        timeout.Message.ShouldContain("the run to complete");
        timeout.Message.ShouldContain("Last value: Running.");
    }

    [Fact]
    public async Task A_cancelled_token_ends_the_wait_before_the_first_probe()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var probes = 0;

        await Should.ThrowAsync<OperationCanceledException>(
            () => WaitUntil.TrueAsync(() => ++probes < 0, "a cancelled wait", cancellationToken: cancelled.Token));

        probes.ShouldBe(0);
    }

    [Fact]
    public async Task Cancelling_during_the_wait_ends_it_rather_than_waiting_out_the_bound()
    {
        using var cancellation = new CancellationTokenSource();
        var probes = 0;

        var wait = WaitUntil.TrueAsync(
            () =>
            {
                if (++probes == 2)
                {
                    cancellation.Cancel();
                }

                return false;
            },
            "a wait cancelled on its second probe",
            TimeSpan.FromMinutes(10),
            cancellation.Token);

        await Should.ThrowAsync<OperationCanceledException>(() => wait);
        probes.ShouldBe(2);
    }

    [Fact]
    public async Task A_probe_that_never_returns_is_cut_at_the_bound()
    {
        var never = new TaskCompletionSource<bool>();

        var timeout = await Should.ThrowAsync<TimeoutException>(
            () => WaitUntil.TrueAsync(() => never.Task, "a stuck probe", TimeSpan.FromMilliseconds(100)));

        timeout.Message.ShouldContain("the probe itself never returned");
    }

    [Fact]
    public async Task Cancelling_during_a_probe_that_never_returns_ends_the_wait()
    {
        using var cancellation = new CancellationTokenSource();
        var never = new TaskCompletionSource<bool>();

        var wait = WaitUntil.TrueAsync(() => never.Task, "a stuck probe", TimeSpan.FromMinutes(10), cancellation.Token);
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => wait);
    }

    [Fact]
    public async Task An_exception_from_the_probe_ends_the_wait_instead_of_counting_as_not_yet()
    {
        var failure = await Should.ThrowAsync<InvalidOperationException>(
            () => WaitUntil.TrueAsync(
                (Func<bool>)(static () => throw new InvalidOperationException("the store is gone")),
                "a probe that throws",
                TimeSpan.FromMinutes(10)));

        failure.Message.ShouldBe("the store is gone");
    }
}
