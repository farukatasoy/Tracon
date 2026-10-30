using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Tracon.Workflows.UnitTests.Fakes;
using static Tracon.Workflows.UnitTests.Fakes.TimeoutRuns;
using static Tracon.Workflows.UnitTests.WorkflowFunctionTimeoutTests;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// A timeout and a retry policy together (failure mode 6):
/// <c>retry(timeout(handler))</c>, so each attempt gets the whole timeout and a timed-out
/// attempt is retried; a caller's stop is never retried.
/// </summary>
public sealed class WorkflowFunctionTimeoutRetryTests
{
    private static readonly WorkflowNodeRetryPolicy RetryTwice = new()
    {
        MaxAttempts = 3,
        InitialDelay = TimeSpan.Zero,
    };

    [Fact]
    public async Task A_timed_out_attempt_is_retried_and_the_next_attempt_gets_the_whole_timeout()
    {
        var clock = new ManualClock();
        var host = Host(clock);
        var attempts = 0;
        var firstStarted = Signal();

        host.AddFunctionWithOptions<List<ChatMessage>, List<ChatMessage>>(
            "flaky",
            async (messages, _, cancellationToken) =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    firstStarted.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); // delay: simulated
                }

                return messages;
            },
            static options =>
            {
                options.Timeout = TimeSpan.FromSeconds(2);
                options.RetryPolicy = RetryTwice;
            });

        await SaveAsync(host, "chain", Function("flaky"));

        var runner = host.CreateRunnerWithTimeProvider(clock, static options => options.RunTimeout = TimeSpan.FromMinutes(10));
        var collecting = Collect(runner, "chain");

        await firstStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await ArmedAsync(clock, TimeSpan.FromSeconds(2));
        clock.Advance(TimeSpan.FromSeconds(2));

        var run = await RunOf(host, await collecting.WaitAsync(TestContext.Current.CancellationToken));

        run.Status.ShouldBe(RunStatus.Completed);
        attempts.ShouldBe(2);
    }

    [Fact]
    public async Task Every_attempt_timing_out_fails_the_run_after_the_last_attempt()
    {
        var clock = new ManualClock();
        var host = Host(clock);
        var attempts = 0;
        var attemptStarted = new SemaphoreSlim(0);

        host.AddFunctionWithOptions<List<ChatMessage>, List<ChatMessage>>(
            "stuck",
            async (messages, _, cancellationToken) =>
            {
                Interlocked.Increment(ref attempts);
                attemptStarted.Release();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); // delay: simulated

                return messages;
            },
            static options =>
            {
                options.Timeout = TimeSpan.FromSeconds(2);
                options.RetryPolicy = RetryTwice;
            });

        await SaveAsync(host, "chain", Function("stuck"));

        var runner = host.CreateRunnerWithTimeProvider(clock, static options => options.RunTimeout = TimeSpan.FromMinutes(10));
        var collecting = Collect(runner, "chain");

        for (var attempt = 0; attempt < RetryTwice.MaxAttempts; attempt++)
        {
            await attemptStarted.WaitAsync(TestContext.Current.CancellationToken);
            await ArmedAsync(clock, TimeSpan.FromSeconds(2));
            clock.Advance(TimeSpan.FromSeconds(2));
        }

        var run = await RunOf(host, await collecting.WaitAsync(TestContext.Current.CancellationToken));

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error!.Type.ShouldBe(TraconWorkflowFunctionTimeoutException.WorkflowFunctionTimeoutErrorType);
        attempts.ShouldBe(RetryTwice.MaxAttempts);
    }

    [Fact]
    public async Task A_caller_that_cancels_a_bounded_and_retried_node_is_not_retried()
    {
        var clock = new ManualClock();
        var host = Host(clock);
        var attempts = 0;
        var started = Signal();

        host.AddFunctionWithOptions<List<ChatMessage>, List<ChatMessage>>(
            "render",
            async (messages, _, cancellationToken) =>
            {
                Interlocked.Increment(ref attempts);
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); // delay: simulated

                return messages;
            },
            static options =>
            {
                options.Timeout = TimeSpan.FromMinutes(5);
                options.RetryPolicy = RetryTwice;
            });

        await SaveAsync(host, "chain", Function("render"));

        using var caller = new CancellationTokenSource();
        var runner = host.CreateRunnerWithTimeProvider(clock, static options => options.RunTimeout = TimeSpan.FromMinutes(10));
        var collecting = Collect(runner, "chain", cancellationToken: caller.Token);

        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await caller.CancelAsync();

        try
        {
            await collecting.WaitAsync(TestContext.Current.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The row decides; see WorkflowFunctionTimeoutReasonTests.
        }

        (await WorkflowRunsAsync(host)).Single().Status.ShouldBe(RunStatus.Canceled);
        attempts.ShouldBe(1);
    }

    [Fact]
    public async Task The_timeout_is_installed_inside_the_retry()
    {
        // Unit level: the wrapper order, without a run. With the order reversed,
        // the whole retry ladder would share one timeout and the second attempt
        // would start with whatever the first left of it.
        var clock = new ManualClock();
        var calls = 0;

        Func<string, IWorkflowContext, CancellationToken, ValueTask<string>> handler = async (input, _, cancellationToken) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); // delay: simulated
            }

            return input;
        };

        var bounded = WorkflowNodeTimeout.Wrap(handler, "f", TimeSpan.FromSeconds(1), clock, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        var retried = WorkflowNodeRetry.Wrap(bounded, RetryTwice, new DefaultRunErrorClassifier(), clock);

        var call = retried("in", null!, CancellationToken.None).AsTask();

        clock.Advance(TimeSpan.FromSeconds(1));

        (await call.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken)).ShouldBe("in");
        calls.ShouldBe(2);
    }
}
