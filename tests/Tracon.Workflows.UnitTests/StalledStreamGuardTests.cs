using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Tracon.Tests.Workflows;
using FrameworkRunStatus = Microsoft.Agents.AI.Workflows.RunStatus;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// The wait of a reopened workflow event stream: it ends when the run is halted and nothing arrives, and it goes on while
/// the run is running.
/// </summary>
/// <remarks>
/// A stream opened after another one has read the run loop's halt signal waits for a signal that never comes (measured with
/// two open questions: one run in ten thousand, eight at a time). The guard reads the framework's status once the stream has
/// been silent for the grace period, so the cases are about what it decides at that moment.
/// </remarks>
public sealed class StalledStreamGuardTests
{
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task A_step_that_answers_within_the_grace_is_returned_and_the_status_is_not_read()
    {
        var clock = new ManualClock();
        var statusReads = 0;
        using var source = new CancellationTokenSource();
        var moving = Task.FromResult(true);

        var answer = await StalledStreamGuard.MoveNextAsync(
            moving,
            () =>
            {
                statusReads++;

                return new ValueTask<FrameworkRunStatus>(FrameworkRunStatus.PendingRequests);
            },
            source,
            Grace,
            clock,
            TestContext.Current.CancellationToken);

        answer.ShouldBeTrue();
        statusReads.ShouldBe(0);
    }

    [Theory]
    [InlineData(FrameworkRunStatus.PendingRequests)]
    [InlineData(FrameworkRunStatus.Idle)]
    public async Task A_silent_stream_of_a_halted_run_is_given_up_when_the_grace_runs_out(FrameworkRunStatus halted)
    {
        var clock = new ManualClock();
        using var source = new CancellationTokenSource();
        var step = new SilentStep(source.Token);

        var waiting = StalledStreamGuard.MoveNextAsync(
            step.Task,
            () => new ValueTask<FrameworkRunStatus>(halted),
            source,
            Grace,
            clock,
            TestContext.Current.CancellationToken).AsTask();

        await WaitUntil.TrueAsync(() => clock.Pending.Contains(Grace), "the guard to start timing the silence");

        waiting.IsCompleted.ShouldBeFalse("the grace has not run out");

        clock.Advance(Grace);

        (await waiting.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken)).ShouldBeFalse();
        source.IsCancellationRequested.ShouldBeTrue("the stream's own token is how the wait is ended");
    }

    [Fact]
    public async Task A_silent_stream_of_a_running_run_keeps_waiting_however_long_a_step_takes()
    {
        var clock = new ManualClock();
        using var source = new CancellationTokenSource();
        var step = new SilentStep(source.Token);
        var status = (int)FrameworkRunStatus.Running;
        var reads = 0;

        var waiting = StalledStreamGuard.MoveNextAsync(
            step.Task,
            () =>
            {
                Interlocked.Increment(ref reads);

                return new ValueTask<FrameworkRunStatus>((FrameworkRunStatus)Volatile.Read(ref status));
            },
            source,
            Grace,
            clock,
            TestContext.Current.CancellationToken).AsTask();

        for (var tick = 1; tick <= 3; tick++)
        {
            await WaitUntil.TrueAsync(() => clock.Pending.Contains(Grace), "the guard to start timing the silence");
            clock.Advance(Grace);
            await WaitUntil.TrueAsync(() => Volatile.Read(ref reads) >= tick, "the guard to read the status");
        }

        waiting.IsCompleted.ShouldBeFalse("the run is still running: the step may take as long as it takes");
        source.IsCancellationRequested.ShouldBeFalse();

        step.Answer(true);

        (await waiting.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_run_that_halts_while_the_stream_is_silent_is_given_up_at_the_next_look()
    {
        var clock = new ManualClock();
        using var source = new CancellationTokenSource();
        var step = new SilentStep(source.Token);
        var status = (int)FrameworkRunStatus.Running;

        var waiting = StalledStreamGuard.MoveNextAsync(
            step.Task,
            () => new ValueTask<FrameworkRunStatus>((FrameworkRunStatus)Volatile.Read(ref status)),
            source,
            Grace,
            clock,
            TestContext.Current.CancellationToken).AsTask();

        await WaitUntil.TrueAsync(() => clock.Pending.Contains(Grace), "the guard to start timing the silence");
        clock.Advance(Grace);
        await WaitUntil.TrueAsync(() => clock.Pending.Contains(Grace), "the guard to start timing again after it looked");

        Volatile.Write(ref status, (int)FrameworkRunStatus.PendingRequests);
        clock.Advance(Grace);

        (await waiting.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_stream_that_throws_a_cancellation_when_it_is_given_up_ends_as_the_end_of_the_stream()
    {
        var clock = new ManualClock();
        using var source = new CancellationTokenSource();
        var step = new SilentStep(source.Token, throwsWhenCancelled: true);

        var waiting = StalledStreamGuard.MoveNextAsync(
            step.Task,
            () => new ValueTask<FrameworkRunStatus>(FrameworkRunStatus.PendingRequests),
            source,
            Grace,
            clock,
            TestContext.Current.CancellationToken).AsTask();

        await WaitUntil.TrueAsync(() => clock.Pending.Contains(Grace), "the guard to start timing the silence");
        clock.Advance(Grace);

        (await waiting.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    [Fact]
    public async Task The_cancellation_of_the_run_is_not_taken_for_a_given_up_wait_and_the_step_is_waited_for()
    {
        var clock = new ManualClock();
        using var run = new CancellationTokenSource();

        // The stream's token is linked to the run's, as the pump links it.
        using var source = CancellationTokenSource.CreateLinkedTokenSource(run.Token);
        var step = new SilentStep(source.Token, throwsWhenCancelled: true);

        var waiting = StalledStreamGuard.MoveNextAsync(
            step.Task,
            () => new ValueTask<FrameworkRunStatus>(FrameworkRunStatus.Running),
            source,
            Grace,
            clock,
            run.Token).AsTask();

        await WaitUntil.TrueAsync(() => clock.Pending.Contains(Grace), "the guard to start timing the silence");

        await run.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => waiting.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken));

        // The stream is disposed next; a step still running under it makes the framework's stream throw.
        step.Task.IsCompleted.ShouldBeTrue("the guard returned while the step was still running");
    }

    [Fact]
    public async Task A_status_read_that_throws_does_not_leave_the_step_running()
    {
        var clock = new ManualClock();
        using var source = new CancellationTokenSource();
        var step = new SilentStep(source.Token);

        var waiting = StalledStreamGuard.MoveNextAsync(
            step.Task,
            () => throw new InvalidOperationException("the status could not be read"),
            source,
            Grace,
            clock,
            TestContext.Current.CancellationToken).AsTask();

        await WaitUntil.TrueAsync(() => clock.Pending.Contains(Grace), "the guard to start timing the silence");
        clock.Advance(Grace);

        await Should.ThrowAsync<InvalidOperationException>(() => waiting.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken));
        step.Task.IsCompleted.ShouldBeTrue("the guard returned while the step was still running");
    }

    /// <summary>
    /// The state the guard exists for, made on purpose with the real framework: a stream opened after another one has read
    /// the run loop's halt signal.
    /// </summary>
    /// <remarks>
    /// The first stream reads the pass to its halt (two requests open, so the status is pending requests). The second one
    /// finds the loop waiting for input and no signal in the channel. Without the guard its step never ends. This is the
    /// race of one run in ten thousand, set up without the race. When a framework release ends the second stream by itself,
    /// the guard has nothing left to guard and this test fails: remove the guard then.
    /// </remarks>
    [Fact]
    public async Task A_stream_opened_after_the_halt_was_read_is_ended_by_the_guard_with_the_real_framework()
    {
        var token = TestContext.Current.CancellationToken;
        var input = new List<ChatMessage> { new(ChatRole.User, "go") };
        var run = await InProcessExecution.RunStreamingAsync(TwoQuestionWorkflow.Build(), input, "stalled-stream", token);

        await using (run)
        {
            await foreach (var _ in run.WatchStreamAsync(blockOnPendingRequest: false, token))
            {
            }

            (await run.GetStatusAsync(token)).ShouldBe(FrameworkRunStatus.PendingRequests, "two requests are open");

            using var source = CancellationTokenSource.CreateLinkedTokenSource(token);
            var second = run.WatchStreamAsync(blockOnPendingRequest: false, source.Token).GetAsyncEnumerator(source.Token);

            await using (second)
            {
                var ended = await StalledStreamGuard.MoveNextAsync(
                    second.MoveNextAsync().AsTask(),
                    () => run.GetStatusAsync(CancellationToken.None),
                    source,
                    TimeSpan.FromMilliseconds(200),
                    TimeProvider.System,
                    token);

                ended.ShouldBeFalse();
                source.IsCancellationRequested.ShouldBeTrue("the guard ended the wait through the stream's own token");
            }
        }
    }

    [Theory]
    [InlineData(FrameworkRunStatus.PendingRequests, true)]
    [InlineData(FrameworkRunStatus.Idle, true)]
    [InlineData(FrameworkRunStatus.Running, false)]
    [InlineData(FrameworkRunStatus.NotStarted, false)]
    [InlineData(FrameworkRunStatus.Ended, false)]
    public void Only_a_run_that_waits_for_input_is_halted(FrameworkRunStatus status, bool halted)
        => StalledStreamGuard.IsHalted(status).ShouldBe(halted);

    /// <summary>A stream step that answers when the test says so, or ends when its token is cancelled.</summary>
    private sealed class SilentStep
    {
        private readonly TaskCompletionSource<bool> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SilentStep(CancellationToken token, bool throwsWhenCancelled = false)
        {
            token.Register(
                () =>
                {
                    if (throwsWhenCancelled)
                    {
                        _answer.TrySetCanceled(token);
                    }
                    else
                    {
                        _answer.TrySetResult(false);
                    }
                });
        }

        public Task<bool> Task => _answer.Task;

        public void Answer(bool value) => _answer.TrySetResult(value);
    }
}
