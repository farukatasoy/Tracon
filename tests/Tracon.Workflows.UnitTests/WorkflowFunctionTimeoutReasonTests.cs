using Microsoft.Extensions.AI;
using Tracon.Workflows.UnitTests.Fakes;
using static Tracon.Workflows.UnitTests.Fakes.TimeoutRuns;
using static Tracon.Workflows.UnitTests.WorkflowFunctionTimeoutTests;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// The node's timeout is named only when the node's OWN delay ended the call (
/// failure mode 5): a run its caller cancelled, or one that reached its
/// own deadline, while a bounded node was running ends <c>Canceled</c> with the reason it
/// already carries.
/// </summary>
/// <remarks>
/// The wrapper sees a cancelled token in all three cases. Reading the reason from the
/// token, a caller's stop would have become a node failure; reading it from the
/// exception, the run's deadline would have.
/// </remarks>
public sealed class WorkflowFunctionTimeoutReasonTests
{
    [Fact]
    public async Task A_caller_that_cancels_while_a_bounded_node_runs_ends_the_run_Canceled_with_no_reason()
    {
        var clock = new ManualClock();
        var host = Host(clock);
        var started = Signal();

        RegisterHanging(host, "render", started, registered: TimeSpan.FromMinutes(5));
        await SaveAsync(host, "chain", Function("render"));

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
            // Whether the stream ends quietly or surfaces the cancellation depends on
            // where the stop lands (WorkflowRunTimeoutReasonTests); the row decides.
        }

        var run = (await WorkflowRunsAsync(host)).Single();

        run.Status.ShouldBe(RunStatus.Canceled);
        run.Error.ShouldBeNull("the caller knows why it stopped; a node timeout would be a wrong reason");
    }

    [Fact]
    public async Task The_runs_own_deadline_while_a_bounded_node_runs_names_the_run_timeout_not_the_node()
    {
        // Two clocks: the run's deadline on one, the node's delay on the other, so
        // only the deadline fires.
        var nodeClock = new ManualClock();
        var runClock = new ManualClock();
        var host = Host(nodeClock);
        var started = Signal();

        RegisterHanging(host, "render", started, registered: TimeSpan.FromMinutes(30));
        await SaveAsync(host, "chain", Function("render"));

        var runner = host.CreateRunnerWithTimeProvider(runClock, static options => options.RunTimeout = TimeSpan.FromMinutes(10));
        var collecting = Collect(runner, "chain");

        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await ArmedAsync(nodeClock, TimeSpan.FromMinutes(30));
        runClock.Advance(TimeSpan.FromMinutes(10));

        var run = await RunOf(host, await collecting.WaitAsync(TestContext.Current.CancellationToken));

        run.Status.ShouldBe(RunStatus.Canceled);
        run.Error.ShouldNotBeNull();
        run.Error.Type.ShouldBe(nameof(TimeoutException));
        run.Error.Message.ShouldContain("Tracon:Workflows:RunTimeout");
    }

    [Fact]
    public async Task When_the_runs_deadline_and_the_nodes_timeout_fire_together_the_run_ends_Canceled()
    {
        // One clock, both limits at ten minutes: the run's deadline was armed first,
        // so it fires first and cancels the node's caller token before the node's own
        // delay lands. The caller's stop wins.
        var clock = new ManualClock();
        var host = Host(clock);
        var started = Signal();

        RegisterHanging(host, "render", started, registered: TimeSpan.FromMinutes(10));
        await SaveAsync(host, "chain", Function("render"));

        var runner = host.CreateRunnerWithTimeProvider(clock, static options => options.RunTimeout = TimeSpan.FromMinutes(10));
        var collecting = Collect(runner, "chain");

        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => clock.Pending.Count(static due => due == TimeSpan.FromMinutes(10)) == 2, "both ten-minute timers");
        clock.Advance(TimeSpan.FromMinutes(10));

        var run = await RunOf(host, await collecting.WaitAsync(TestContext.Current.CancellationToken));

        run.Status.ShouldBe(RunStatus.Canceled);
        run.Error!.Type.ShouldBe(nameof(TimeoutException));
    }

    [Fact]
    public async Task A_node_that_returns_in_time_is_untouched_by_its_timeout()
    {
        var clock = new ManualClock();
        var host = Host(clock);

        host.AddFunctionWithOptions<List<ChatMessage>, List<ChatMessage>>(
            "quick",
            static (messages, _, _) => new ValueTask<List<ChatMessage>>(messages),
            static options => options.Timeout = TimeSpan.FromSeconds(1));

        await SaveAsync(host, "chain", Function("quick"));

        var runner = host.CreateRunnerWithTimeProvider(clock, static options => options.RunTimeout = TimeSpan.FromMinutes(10));
        var run = await RunOf(host, await Collect(runner, "chain"));

        run.Status.ShouldBe(RunStatus.Completed);
        run.Error.ShouldBeNull();

        // The node's delay was disarmed when the call returned; only the run's
        // deadline is left, and the run is over.
        clock.Pending.ShouldNotContain(TimeSpan.FromSeconds(1));
    }
}
