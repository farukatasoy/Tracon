using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Tools;

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
                await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None);
                return "too late";
            },
            "slow_tool");

        var wrapped = new TimeoutAIFunction(inner, TimeSpan.FromMilliseconds(200), NullLogger<TimeoutAIFunction>.Instance);

        var stopwatch = Stopwatch.StartNew();

        var exception = await Should.ThrowAsync<AgentPrismToolTimeoutException>(
            async () => await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal)));

        stopwatch.Stop();

        // Generous upper bound: the point is that the WAIT is bounded by the
        // configured timeout, not by the tool's real 30-second body.
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        exception.ToolName.ShouldBe("slow_tool");
        exception.Timeout.ShouldBe(TimeSpan.FromMilliseconds(200));
        exception.ErrorType.ShouldBe(AgentPrismToolTimeoutException.ToolTimeoutErrorType);
    }

    [Fact]
    public async Task A_call_that_honors_cancellation_propagates_the_real_cancellation_not_a_timeout()
    {
        var inner = AIFunctionFactory.Create(
            async (CancellationToken cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                return "unreachable";
            },
            "cooperative_tool");

        var wrapped = new TimeoutAIFunction(inner, TimeSpan.FromSeconds(30), NullLogger<TimeoutAIFunction>.Instance);

        using var cts = new CancellationTokenSource();
        var invocation = wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal), cts.Token).AsTask();

        await cts.CancelAsync();

        // The caller's OWN cancellation must surface as itself, not be
        // reinterpreted as AgentPrismToolTimeoutException.
        await Should.ThrowAsync<OperationCanceledException>(async () => await invocation);
    }
}
