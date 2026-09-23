using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon.Core.UnitTests.Tools;

/// <summary>
/// Verifies <see cref="TimeoutAIFunction"/>: a call that settles in time
/// passes through unchanged, a call that outlives its timeout is cut short
/// even when its body never reads the cancellation token (Manual Case 8,
/// docs/arsiv/fazlar/69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md), and a real caller
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
    public async Task A_call_that_never_reads_its_token_is_still_cut_off_near_the_timeout()
    {
        // The tool body deliberately ignores the cancellation token — the
        // documented, non-cooperative case this class must still bound.
        var inner = AIFunctionFactory.Create(
            async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None); // delay: simulated
                return "too late";
            },
            "slow_tool");

        var wrapped = new TimeoutAIFunction(inner, TimeSpan.FromMilliseconds(200), NullLogger<TimeoutAIFunction>.Instance);

        var stopwatch = Stopwatch.StartNew();

        var exception = await Should.ThrowAsync<TraconToolTimeoutException>(
            async () => await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal)));

        stopwatch.Stop();

        // Generous upper bound: the point is that the WAIT is bounded by the
        // configured timeout, not by the tool's real 30-second body.
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        exception.ToolName.ShouldBe("slow_tool");
        exception.Timeout.ShouldBe(TimeSpan.FromMilliseconds(200));
        exception.ErrorType.ShouldBe(TraconToolTimeoutException.ToolTimeoutErrorType);
    }

    [Fact]
    public async Task The_timeout_cancels_the_tool_body_instead_of_only_giving_up_on_it()
    {
        // 🚨 Measured on the old code: NOTHING was cancelled. A body that read
        // its token on every wait still ran its full 3 seconds against a 300ms
        // limit — the wrapper stopped waiting and left the work running, so a
        // cooperative tool went on spending money nobody was waiting for.
        var outcome = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var inner = AIFunctionFactory.Create(
            async (CancellationToken cancellationToken) =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken); // delay: simulated
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

        var wrapped = new TimeoutAIFunction(inner, TimeSpan.FromMilliseconds(200), NullLogger<TimeoutAIFunction>.Instance);

        await Should.ThrowAsync<TraconToolTimeoutException>(
            async () => await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal)));

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
                await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None); // delay: simulated

                return "too late";
            },
            "brief_tool");

        var wrapped = new TimeoutAIFunction(inner, TimeSpan.FromMilliseconds(500), NullLogger<TimeoutAIFunction>.Instance);

        var exception = await Should.ThrowAsync<TraconToolTimeoutException>(
            async () => await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal)));

        exception.Message.ShouldBe("Tool 'brief_tool' did not complete within 500ms.");
        exception.Message.ShouldNotContain("0s");
    }

    [Fact]
    public async Task A_whole_second_timeout_keeps_reading_in_seconds()
    {
        var inner = AIFunctionFactory.Create(
            async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None); // delay: simulated

                return "too late";
            },
            "slow_tool");

        var wrapped = new TimeoutAIFunction(inner, TimeSpan.FromSeconds(1), NullLogger<TimeoutAIFunction>.Instance);

        var exception = await Should.ThrowAsync<TraconToolTimeoutException>(
            async () => await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal)));

        exception.Message.ShouldBe("Tool 'slow_tool' did not complete within 1s.");
    }

    [Fact]
    public async Task A_call_that_honors_cancellation_propagates_the_real_cancellation_not_a_timeout()
    {
        var inner = AIFunctionFactory.Create(
            async (CancellationToken cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken); // delay: simulated
                return "unreachable";
            },
            "cooperative_tool");

        var wrapped = new TimeoutAIFunction(inner, TimeSpan.FromSeconds(30), NullLogger<TimeoutAIFunction>.Instance);

        using var cts = new CancellationTokenSource();
        var invocation = wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal), cts.Token).AsTask();

        await cts.CancelAsync();

        // The caller's OWN cancellation must surface as itself, not be
        // reinterpreted as TraconToolTimeoutException.
        await Should.ThrowAsync<OperationCanceledException>(async () => await invocation);
    }
}
