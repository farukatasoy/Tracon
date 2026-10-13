using Microsoft.Extensions.AI;
using Tracon.Workflows.UnitTests.Fakes;
using static Tracon.Workflows.UnitTests.Fakes.TimeoutRuns;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// A function node's timeout (F-303): which value applies, what the run ends with, and
/// that the handler's token is really cancelled.
/// </summary>
/// <remarks>
/// <para>
/// Every case runs the real <c>AddWorkflowFunction</c> registration, so the wrapper a
/// consumer gets is the one under test, and a <see cref="ManualClock"/> whose timers
/// honour their due time: a case that asserts "the node's five seconds, not the
/// registration's minute" has to be able to tell them apart.
/// </para>
/// <para>
/// Before phase 196 a function node had no timeout at all: only the run's deadline
/// stopped it, ended the run <c>Canceled</c>, and gave no node's name.
/// </para>
/// </remarks>
public sealed class WorkflowFunctionTimeoutTests
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(10);

    [Fact]
    public async Task A_function_past_its_timeout_fails_the_run_with_the_stable_type_and_no_later_node_runs()
    {
        var clock = new ManualClock();
        var host = Host(clock);
        var started = Signal();
        var laterRan = false;

        host.AddFunctionWithOptions<List<ChatMessage>, List<ChatMessage>>(
            "slow",
            async (messages, _, cancellationToken) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); // delay: simulated

                return messages;
            },
            static options => options.Timeout = TimeSpan.FromSeconds(2));

        host.AddFunction<List<ChatMessage>, List<ChatMessage>>(
            "later",
            (messages, _, _) =>
            {
                laterRan = true;

                return new ValueTask<List<ChatMessage>>(messages);
            });

        await SaveAsync(host, "chain", Function("slow"), Function("later"));

        var runner = host.CreateRunnerWithTimeProvider(clock, static options => options.RunTimeout = RunTimeout);
        var collecting = Collect(runner, "chain");

        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await ArmedAsync(clock, TimeSpan.FromSeconds(2));
        clock.Advance(TimeSpan.FromSeconds(2));

        var events = await collecting.WaitAsync(TestContext.Current.CancellationToken);
        var run = await RunOf(host, events);

        // A step that did not answer FAILS the run (open question 2-A): the
        // node's own limit is not a stop somebody asked for.
        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull();
        run.Error.Type.ShouldBe(TraconWorkflowFunctionTimeoutException.WorkflowFunctionTimeoutErrorType);
        run.Error.Message.ShouldContain("'slow'");
        run.Error.Message.ShouldContain("2 s");
        laterRan.ShouldBeFalse("a node after the one that timed out must not run");
        events[^1].Type.ShouldBe(RunEventType.RunFailed);
    }

    [Fact]
    public async Task The_handlers_token_is_cancelled_at_the_timeout_and_a_listening_body_stops_there()
    {
        // K-805: a bound that only ends the wait cancels nothing. The body below
        // listens to its token; once the timeout fires, nothing after its await
        // may run.
        var clock = new ManualClock();
        var host = Host(clock);
        var started = Signal();
        var tokenCancelled = Signal();
        var ranPastTheWait = false;

        host.AddFunctionWithOptions<List<ChatMessage>, List<ChatMessage>>(
            "listening",
            async (messages, _, cancellationToken) =>
            {
                using var registration = cancellationToken.Register(static state => ((TaskCompletionSource)state!).TrySetResult(), tokenCancelled);
                started.TrySetResult();

                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); // delay: simulated
                ranPastTheWait = true;

                return messages;
            },
            static options => options.Timeout = TimeSpan.FromSeconds(3));

        await SaveAsync(host, "chain", Function("listening"));

        var runner = host.CreateRunnerWithTimeProvider(clock, static options => options.RunTimeout = RunTimeout);
        var collecting = Collect(runner, "chain");

        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await ArmedAsync(clock, TimeSpan.FromSeconds(3));
        clock.Advance(TimeSpan.FromSeconds(3));

        await tokenCancelled.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await collecting.WaitAsync(TestContext.Current.CancellationToken);

        ranPastTheWait.ShouldBeFalse("the body's token must be cancelled at the timeout, not only the run's wait");
    }

    [Fact]
    public async Task A_nodes_own_timeout_replaces_the_one_the_function_was_registered_with()
    {
        var clock = new ManualClock();
        var host = Host(clock);
        var started = Signal();

        RegisterHanging(host, "render", started, registered: TimeSpan.FromMinutes(1));

        await SaveAsync(host, "chain", Function("render", timeout: TimeSpan.FromSeconds(5)));

        var runner = host.CreateRunnerWithTimeProvider(clock, static options => options.RunTimeout = RunTimeout);
        var collecting = Collect(runner, "chain");

        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await ArmedAsync(clock, TimeSpan.FromSeconds(5));

        // The node's delay is armed for its own five seconds, not the minute.
        clock.Pending.ShouldContain(TimeSpan.FromSeconds(5));
        clock.Pending.ShouldNotContain(TimeSpan.FromMinutes(1));

        clock.Advance(TimeSpan.FromSeconds(5));

        var run = await RunOf(host, await collecting.WaitAsync(TestContext.Current.CancellationToken));

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error!.Type.ShouldBe(TraconWorkflowFunctionTimeoutException.WorkflowFunctionTimeoutErrorType);
        run.Error.Message.ShouldContain("5 s");
    }

    [Fact]
    public async Task A_node_without_a_timeout_of_its_own_uses_the_registered_one()
    {
        var clock = new ManualClock();
        var host = Host(clock);
        var started = Signal();

        RegisterHanging(host, "render", started, registered: TimeSpan.FromSeconds(30));

        await SaveAsync(host, "chain", Function("render"));

        var runner = host.CreateRunnerWithTimeProvider(clock, static options => options.RunTimeout = RunTimeout);
        var collecting = Collect(runner, "chain");

        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await ArmedAsync(clock, TimeSpan.FromSeconds(30));

        // One second short: still running.
        clock.Advance(TimeSpan.FromSeconds(29));
        collecting.IsCompleted.ShouldBeFalse();

        clock.Advance(TimeSpan.FromSeconds(1));

        var run = await RunOf(host, await collecting.WaitAsync(TestContext.Current.CancellationToken));

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error!.Message.ShouldContain("30 s");
    }

    [Fact]
    public async Task A_function_with_no_timeout_anywhere_arms_no_delay_and_runs_to_its_end()
    {
        // The other half: without a timeout the wrapper is not installed, so a
        // slow node keeps today's behavior - only the run's deadline bounds it.
        var clock = new ManualClock();
        var host = Host(clock);
        var started = Signal();
        var release = Signal();

        host.AddFunctionWithOptions<List<ChatMessage>, List<ChatMessage>>(
            "plain",
            async (messages, _, _) =>
            {
                started.TrySetResult();
                await release.Task.ConfigureAwait(false);

                return messages;
            },
            static options => options.Description = "No timeout.");

        await SaveAsync(host, "chain", Function("plain"));

        var runner = host.CreateRunnerWithTimeProvider(clock, static options => options.RunTimeout = RunTimeout);
        var collecting = Collect(runner, "chain");

        await started.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Only the run's own deadline is armed.
        clock.Pending.ShouldBe([RunTimeout]);

        release.TrySetResult();

        var run = await RunOf(host, await collecting.WaitAsync(TestContext.Current.CancellationToken));

        run.Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task The_catalog_lists_the_registered_timeout()
    {
        var host = Host(new ManualClock());

        host.AddFunctionWithOptions<List<ChatMessage>, List<ChatMessage>>(
            "render",
            static (messages, _, _) => new ValueTask<List<ChatMessage>>(messages),
            static options => options.Timeout = TimeSpan.FromSeconds(45));

        host.AddFunction<List<ChatMessage>, List<ChatMessage>>(
            "plain",
            static (messages, _, _) => new ValueTask<List<ChatMessage>>(messages));

        var functions = host.Functions.List();

        functions.Single(static function => string.Equals(function.Name, "render", StringComparison.Ordinal)).Timeout.ShouldBe(TimeSpan.FromSeconds(45));
        functions.Single(static function => string.Equals(function.Name, "plain", StringComparison.Ordinal)).Timeout.ShouldBeNull();
    }

    /// <summary>Registers a function that hangs on its token until cancelled.</summary>
    internal static void RegisterHanging(WorkflowTestHost host, string name, TaskCompletionSource started, TimeSpan? registered)
        => host.AddFunctionWithOptions<List<ChatMessage>, List<ChatMessage>>(
            name,
            async (messages, _, cancellationToken) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); // delay: simulated

                return messages;
            },
            options => options.Timeout = registered);

    internal static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Waits until a timer due in <paramref name=due/> is armed. The handler signals
    /// that it started BEFORE the wrapper arms its delay (the handler runs to its first
    /// await inside the call), so advancing the clock on that signal alone could miss it.
    /// </summary>
    internal static Task ArmedAsync(ManualClock clock, TimeSpan due)
        => WaitUntil.TrueAsync(() => clock.Pending.Contains(due), $"a timer due in {due} to be armed");
}
