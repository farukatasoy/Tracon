using Microsoft.Extensions.AI;
using Tracon.Workflows.UnitTests.Fakes;
using static Tracon.Workflows.UnitTests.Fakes.TimeoutRuns;
using static Tracon.Workflows.UnitTests.WorkflowFunctionTimeoutTests;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// Two runs share one handler closure (F-303, failure mode 7): the factory runs once,
/// and every compile of every workflow that names the function gets the same handler.
/// One run's timeout must cancel only that run's call.
/// </summary>
/// <remarks>
/// The wrapper keeps its token source per CALL. A source kept per registration - the
/// natural shape of a closure built once - would cancel every run in flight at the
/// first timeout.
/// </remarks>
public sealed class WorkflowFunctionTimeoutConcurrencyTests
{
    [Fact]
    public async Task One_runs_timeout_does_not_cancel_the_other_runs_call_of_the_same_function()
    {
        var clock = new ManualClock();
        var host = Host(clock);
        var calls = new System.Collections.Concurrent.ConcurrentDictionary<string, (TaskCompletionSource Started, CancellationToken Token)>(StringComparer.Ordinal);
        var releaseFast = Signal();

        host.AddFunctionWithOptions<List<ChatMessage>, List<ChatMessage>>(
            "render",
            async (messages, _, cancellationToken) =>
            {
                var label = messages[^1].Text;
                var started = calls.GetOrAdd(label, static _ => (Signal(), default)).Started;

                calls[label] = (started, cancellationToken);
                started.TrySetResult();

                if (string.Equals(label, "slow", StringComparison.Ordinal))
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); // delay: simulated
                }
                else
                {
                    await releaseFast.Task.ConfigureAwait(false);
                }

                return messages;
            },
            static options => options.Timeout = TimeSpan.FromMinutes(1));

        // The same function in two workflows, with different node timeouts.
        await SaveAsync(host, "short", Function("render", timeout: TimeSpan.FromSeconds(2)));
        await SaveAsync(host, "long", Function("render", timeout: TimeSpan.FromSeconds(30)));

        calls["slow"] = (Signal(), default);
        calls["fast"] = (Signal(), default);

        var runner = host.CreateRunnerWithTimeProvider(clock, static options => options.RunTimeout = TimeSpan.FromMinutes(10));
        var slow = Collect(runner, "short", "slow");
        var fast = Collect(runner, "long", "fast");

        await calls["slow"].Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await calls["fast"].Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await ArmedAsync(clock, TimeSpan.FromSeconds(2));
        await ArmedAsync(clock, TimeSpan.FromSeconds(30));

        clock.Advance(TimeSpan.FromSeconds(2));

        var slowRun = await RunOf(host, await slow.WaitAsync(TestContext.Current.CancellationToken));

        slowRun.Status.ShouldBe(RunStatus.Failed);
        slowRun.Error!.Type.ShouldBe(TraconWorkflowFunctionTimeoutException.WorkflowFunctionTimeoutErrorType);

        // The other call's token is untouched, and its run completes.
        calls["fast"].Token.IsCancellationRequested.ShouldBeFalse();

        releaseFast.TrySetResult();

        var fastRun = await RunOf(host, await fast.WaitAsync(TestContext.Current.CancellationToken));

        fastRun.Status.ShouldBe(RunStatus.Completed);
    }
}
