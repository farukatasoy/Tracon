using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon.Core.UnitTests.Tools;

/// <summary>
/// Verifies <see cref="TimeoutAIFunction"/>: a call that settles in time
/// passes through unchanged, a call that outlives its timeout is cut short
/// even when its body never reads the cancellation token,
/// and a real caller
/// cancellation is never mislabeled as a timeout.
/// </summary>
public sealed class TimeoutAIFunctionTests
{
    [Fact]
    public async Task A_call_that_settles_in_time_returns_its_own_result()
    {
        var inner = AIFunctionFactory.Create(() => "in transit", "get_order");
        var wrapped = new TimeoutAIFunction(inner, TimeSpan.FromSeconds(30), NullLogger<TimeoutAIFunction>.Instance);

        var result = await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        result?.ToString().ShouldBe("in transit");
    }

    [Fact]
    public async Task A_call_that_never_reads_its_token_is_still_cut_off_when_the_clock_reaches_the_timeout()
    {
        // The tool body deliberately ignores the cancellation token — the
        // documented, non-cooperative case this class must still bound.
        var inner = AIFunctionFactory.Create(
            async () =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None); // delay: simulated
                return "too late";
            },
            "slow_tool");

        var clock = new ManualClock();
        var timeout = TimeSpan.FromMinutes(5);

        var exception = await TimeOutAsync(inner, timeout, clock);

        // The WAIT is bounded by the configured timeout, on the clock the wrapper was given: the clock moved by
        // exactly that, and the tool's own body never ends.
        clock.Steps.ShouldBe([timeout]);
        exception.ToolName.ShouldBe("slow_tool");
        exception.Timeout.ShouldBe(timeout);
        exception.ErrorType.ShouldBe(TraconToolTimeoutException.ToolTimeoutErrorType);
    }

    [Fact]
    public async Task The_timeout_cancels_the_tool_body_instead_of_only_giving_up_on_it()
    {
        // 🚨 Measured on the old code: NOTHING was cancelled. A body that read
        // its token on every wait still ran to its end after the limit — the wrapper stopped waiting and left the
        // work running, so a cooperative tool went on spending money nobody was waiting for.
        var outcome = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var inner = AIFunctionFactory.Create(
            async (CancellationToken cancellationToken) =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); // delay: simulated
                    outcome.TrySetResult("ran to completion");

                    return "too late";
                }
                catch (OperationCanceledException)
                {
                    outcome.TrySetResult("cancelled");

                    throw;
                }
            },
            "cooperative_slow_tool");

        await TimeOutAsync(inner, TimeSpan.FromMinutes(5), new ManualClock());

        var result = await outcome.Task.WaitAsync(TimeSpan.FromSeconds(10));

        result.ShouldBe("cancelled");
    }

    [Fact]
    public async Task A_sub_second_timeout_is_reported_in_milliseconds_not_as_zero_seconds()
    {
        // The message goes to the MODEL. A whole-second format turned every
        // sub-second bound into "did not complete within 0s", which is not a
        // sentence a model can act on.
        var inner = AIFunctionFactory.Create(
            async () =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None); // delay: simulated

                return "too late";
            },
            "brief_tool");

        var exception = await TimeOutAsync(inner, TimeSpan.FromMilliseconds(500), new ManualClock());

        exception.Message.ShouldBe("Tool 'brief_tool' did not complete within 500ms.");
        exception.Message.ShouldNotContain("0s");
    }

    [Fact]
    public async Task A_whole_second_timeout_keeps_reading_in_seconds()
    {
        var inner = AIFunctionFactory.Create(
            async () =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None); // delay: simulated

                return "too late";
            },
            "slow_tool");

        var exception = await TimeOutAsync(inner, TimeSpan.FromSeconds(1), new ManualClock());

        exception.Message.ShouldBe("Tool 'slow_tool' did not complete within 1s.");
    }

    [Fact]
    public async Task A_call_that_honors_cancellation_propagates_the_real_cancellation_not_a_timeout()
    {
        var inner = AIFunctionFactory.Create(
            async (CancellationToken cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); // delay: simulated
                return "unreachable";
            },
            "cooperative_tool");

        var clock = new ManualClock();
        var wrapped = new TimeoutAIFunction(inner, TimeSpan.FromSeconds(30), NullLogger<TimeoutAIFunction>.Instance, clock);

        using var cts = new CancellationTokenSource();
        var invocation = wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal), cts.Token).AsTask();

        await cts.CancelAsync();

        // The caller's OWN cancellation must surface as itself, not be
        // reinterpreted as TraconToolTimeoutException. The clock never moved, so no timeout can have fired, and the
        // timeout's timer does not outlive the call.
        await Should.ThrowAsync<OperationCanceledException>(async () => await invocation);
        clock.RunningTimers.ShouldBe(0);
    }

    /// <summary>Calls a tool that never ends and moves the wrapper's clock to its timeout, which is what ends the call.</summary>
    private static async Task<TraconToolTimeoutException> TimeOutAsync(AIFunction inner, TimeSpan timeout, ManualClock clock)
    {
        var wrapped = new TimeoutAIFunction(inner, timeout, NullLogger<TimeoutAIFunction>.Instance, clock);
        var invocation = wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal)).AsTask();

        return await Should.ThrowAsync<TraconToolTimeoutException>(
            async () => await clock.AdvanceUntilAsync(invocation, within: timeout));
    }

    /// <summary>
    /// The delay behind the timeout refuses more than about 49.7 days at call
    /// time, after the tool body has started. Every per-tool timeout (code registration,
    /// <c>[TraconTool(TimeoutSeconds)]</c>, the tools default) passes through here, so
    /// the range is checked when the wrapper is built.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(50 * 24 * 60 * 60)]
    public void A_timeout_the_delay_cannot_take_is_refused_when_the_wrapper_is_built(int seconds)
    {
        var inner = AIFunctionFactory.Create(() => "ok", "any_tool");

        Should.Throw<ArgumentOutOfRangeException>(
            () => new TimeoutAIFunction(inner, TimeSpan.FromSeconds(seconds), NullLogger<TimeoutAIFunction>.Instance));
    }

    [Fact]
    public void The_longest_timeout_the_delay_takes_is_accepted()
        => Should.NotThrow(() => new TimeoutAIFunction(
            AIFunctionFactory.Create(() => "ok", "any_tool"),
            TimeSpan.FromDays(49),
            NullLogger<TimeoutAIFunction>.Instance));
}
