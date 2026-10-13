using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Tracon.Workflows.UnitTests.Fakes;
using static Tracon.Workflows.UnitTests.Fakes.TimeoutRuns;
using static Tracon.Workflows.UnitTests.WorkflowFunctionTimeoutTests;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// A function that ignores its token (F-303, failure mode 4): the run must not wait for
/// it, its late end must not touch the closed run, and a late fault must not go
/// unobserved.
/// </summary>
/// <remarks>
/// Measured before the wrapper existed: a function that ignored its token held the run
/// until it returned, however long that was - the run's own deadline cancelled the token
/// and the runner still waited for the node to finish its super-step.
/// </remarks>
public sealed class WorkflowFunctionTimeoutLateCompletionTests
{
    [Fact]
    public async Task The_run_ends_at_the_timeout_while_a_deaf_body_still_runs_and_its_late_end_is_only_logged()
    {
        var clock = new ManualClock();
        var logs = new CapturingLoggerProvider();
        var host = Host(clock, logs);
        var started = Signal();
        var release = Signal();
        var returned = Signal();

        host.AddFunctionWithOptions<List<ChatMessage>, List<ChatMessage>>(
            "deaf",
            async (messages, _, _) =>
            {
                started.TrySetResult();

                // Ignores its token: only the test ends this wait.
                await release.Task.ConfigureAwait(false);
                returned.TrySetResult();

                return [new ChatMessage(ChatRole.User, "late")];
            },
            static options => options.Timeout = TimeSpan.FromSeconds(2));

        await SaveAsync(host, "chain", Function("deaf"));

        var runner = host.CreateRunnerWithTimeProvider(clock, static options => options.RunTimeout = TimeSpan.FromMinutes(10));
        var collecting = Collect(runner, "chain");

        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await ArmedAsync(clock, TimeSpan.FromSeconds(2));
        clock.Advance(TimeSpan.FromSeconds(2));

        // The run ends although the body has not returned.
        var events = await collecting.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        returned.Task.IsCompleted.ShouldBeFalse("the body ignores its token and is still running");

        var closed = await RunOf(host, events);

        closed.Status.ShouldBe(RunStatus.Failed);
        closed.Error!.Type.ShouldBe(TraconWorkflowFunctionTimeoutException.WorkflowFunctionTimeoutErrorType);

        release.TrySetResult();
        await returned.Task.WaitAsync(TestContext.Current.CancellationToken);

        await WaitUntil.TrueAsync(
            () => logs.Entries.Any(static entry =>
                entry.Level == LogLevel.Warning &&
                entry.Message.Contains("its result is discarded", StringComparison.Ordinal)),
            "the late return to be logged");

        // The late result reaches nothing: the closed run is exactly as it was.
        var after = await RunOf(host, events);

        after.Status.ShouldBe(closed.Status);
        after.EventCount.ShouldBe(closed.EventCount);
        after.CompletedAt.ShouldBe(closed.CompletedAt);
        after.Error.ShouldBe(closed.Error);
        (await host.RunStore.ReadEventsAsync(events[0].RunId, 0, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken))
            .Count.ShouldBe((int)closed.EventCount);
    }

    /// <summary>
    /// The run stopped waiting because its caller cancelled it, not because the node ran
    /// out of time: the late end must not be logged as "past its timeout" (audit). The
    /// same holds for the run's own deadline, which reaches the node the same way.
    /// </summary>
    [Fact]
    public async Task A_deaf_bodys_late_end_after_its_caller_stopped_is_not_logged_as_past_its_timeout()
    {
        var clock = new ManualClock();
        var logs = new CapturingLoggerProvider();
        var host = Host(clock, logs);
        var started = Signal();
        var release = Signal();
        var returned = Signal();

        host.AddFunctionWithOptions<List<ChatMessage>, List<ChatMessage>>(
            "deaf",
            async (messages, _, _) =>
            {
                started.TrySetResult();

                // Ignores its token: only the test ends this wait.
                await release.Task.ConfigureAwait(false);
                returned.TrySetResult();

                return messages;
            },
            static options => options.Timeout = TimeSpan.FromMinutes(5));

        await SaveAsync(host, "chain", Function("deaf"));

        using var caller = new CancellationTokenSource();
        var runner = host.CreateRunnerWithTimeProvider(clock, static options => options.RunTimeout = TimeSpan.FromMinutes(10));
        var collecting = Collect(runner, "chain", cancellationToken: caller.Token);

        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await ArmedAsync(clock, TimeSpan.FromMinutes(5));
        await caller.CancelAsync();

        try
        {
            await collecting.WaitAsync(TestContext.Current.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Where the stop lands decides whether the stream surfaces it; the log decides here.
        }

        release.TrySetResult();
        await returned.Task.WaitAsync(TestContext.Current.CancellationToken);

        await WaitUntil.TrueAsync(
            () => logs.Entries.Any(static entry => entry.Message.Contains("its result is discarded", StringComparison.Ordinal)),
            "the late return to be logged");

        var late = logs.Entries.Single(static entry => entry.Message.Contains("its result is discarded", StringComparison.Ordinal));

        late.Message.ShouldNotContain("past its timeout");
        late.Message.ShouldContain("cancelled or reached its deadline");
    }

    [Fact]
    public async Task A_deaf_body_that_faults_late_is_observed_and_logged_not_left_unobserved()
    {
        var clock = new ManualClock();
        var logs = new CapturingLoggerProvider();
        var host = Host(clock, logs);
        var started = Signal();
        var release = Signal();
        const string LateFault = "late fault of the deaf body";

        host.AddFunctionWithOptions<List<ChatMessage>, List<ChatMessage>>(
            "deaf",
            async (_, _, _) =>
            {
                started.TrySetResult();
                await release.Task.ConfigureAwait(false);

                throw new InvalidOperationException(LateFault);
            },
            static options => options.Timeout = TimeSpan.FromSeconds(2));

        await SaveAsync(host, "chain", Function("deaf"));

        var unobserved = new List<Exception>();
        EventHandler<UnobservedTaskExceptionEventArgs> collect = (_, args) =>
        {
            lock (unobserved)
            {
                unobserved.AddRange(args.Exception.Flatten().InnerExceptions);
            }
        };

        TaskScheduler.UnobservedTaskException += collect;

        try
        {
            var runner = host.CreateRunnerWithTimeProvider(clock, static options => options.RunTimeout = TimeSpan.FromMinutes(10));
            var collecting = Collect(runner, "chain");

            await started.Task.WaitAsync(TestContext.Current.CancellationToken);
            await ArmedAsync(clock, TimeSpan.FromSeconds(2));
            clock.Advance(TimeSpan.FromSeconds(2));

            (await RunOf(host, await collecting.WaitAsync(TestContext.Current.CancellationToken))).Status.ShouldBe(RunStatus.Failed);

            release.TrySetResult();

            await WaitUntil.TrueAsync(
                () => logs.Entries.Any(static entry =>
                    entry.Level == LogLevel.Warning &&
                    string.Equals(entry.Exception?.InnerException?.Message, LateFault, StringComparison.Ordinal)),
                "the late fault to be logged with its exception");

            // Whatever was left unobserved surfaces at the next collection.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= collect;
        }

        lock (unobserved)
        {
            unobserved.ShouldNotContain(static exception => string.Equals(exception.Message, LateFault, StringComparison.Ordinal));
        }
    }
}
