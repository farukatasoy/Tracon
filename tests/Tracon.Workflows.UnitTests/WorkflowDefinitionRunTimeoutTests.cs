using Microsoft.Extensions.AI;
using Tracon.Workflows.UnitTests.Fakes;
using static Tracon.Workflows.UnitTests.Fakes.TimeoutRuns;
using static Tracon.Workflows.UnitTests.WorkflowFunctionTimeoutTests;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// A definition's own run timeout (F-405, failure mode 26): it re-arms the run's
/// deadline from the start of the run, the host's ceiling bounds it, and it bounds the
/// wait for a concurrency slot too.
/// </summary>
/// <remarks>
/// <para>
/// The deadline is armed with the installation's value when the run starts, before the
/// definition is read. Measured (phase 196, step 0): the definition was resolved only
/// after the slot wait, so a definition allowed an hour still timed out in the queue on
/// the installation's ten minutes. Setup now runs before the slot wait, and the deadline
/// is moved as soon as the definition is read.
/// </para>
/// <para>
/// The save-time ceiling (<c>PUT</c> → <c>400</c>) is <c>WorkflowTimeoutSaveTests</c> in
/// the functional tests.
/// </para>
/// </remarks>
public sealed class WorkflowDefinitionRunTimeoutTests
{
    private static readonly TimeSpan Installation = TimeSpan.FromMinutes(10);

    [Fact]
    public async Task A_shorter_run_timeout_moves_the_deadline_and_the_reason_names_the_definition()
    {
        var clock = new ManualClock();
        var host = Host(clock);
        var started = Signal();

        RegisterHanging(host, "render", started, registered: null);
        await SaveRunTimeoutAsync(host, TimeSpan.FromMinutes(1));

        var runner = host.CreateRunnerWithTimeProvider(clock, static options => options.RunTimeout = Installation);
        var collecting = Collect(runner, "chain");

        await started.Task.WaitAsync(TestContext.Current.CancellationToken);

        clock.Pending.ShouldBe([TimeSpan.FromMinutes(1)], "the installation's ten minutes are replaced, not joined");

        clock.Advance(TimeSpan.FromMinutes(1));

        var run = await RunOf(host, await collecting.WaitAsync(TestContext.Current.CancellationToken));

        run.Status.ShouldBe(RunStatus.Canceled);
        run.Error!.Type.ShouldBe(nameof(TimeoutException));
        run.Error.Message.ShouldContain("'runTimeout'");
        run.Error.Message.ShouldNotContain("Raise the 'Tracon:Workflows:RunTimeout' value");
    }

    [Fact]
    public async Task A_longer_run_timeout_within_the_hosts_ceiling_extends_the_deadline()
    {
        var clock = new ManualClock();
        var host = Host(clock);
        var started = Signal();

        RegisterHanging(host, "render", started, registered: null);
        await SaveRunTimeoutAsync(host, TimeSpan.FromMinutes(30));

        var runner = host.CreateRunnerWithTimeProvider(clock, static options =>
        {
            options.RunTimeout = Installation;
            options.MaxRunTimeout = TimeSpan.FromHours(1);
        });
        var collecting = Collect(runner, "chain");

        await started.Task.WaitAsync(TestContext.Current.CancellationToken);

        clock.Pending.ShouldBe([TimeSpan.FromMinutes(30)]);

        // Past the installation's value: still running.
        clock.Advance(TimeSpan.FromMinutes(15));
        collecting.IsCompleted.ShouldBeFalse();

        clock.Advance(TimeSpan.FromMinutes(15));

        (await RunOf(host, await collecting.WaitAsync(TestContext.Current.CancellationToken))).Status.ShouldBe(RunStatus.Canceled);
    }

    [Fact]
    public async Task A_stored_run_timeout_above_a_lowered_ceiling_still_runs_cut_to_the_ceiling()
    {
        var clock = new ManualClock();
        var host = Host(clock);
        var started = Signal();

        RegisterHanging(host, "render", started, registered: null);

        // Saved while the host allowed it; the store does not check the ceiling.
        await SaveRunTimeoutAsync(host, TimeSpan.FromMinutes(45));

        var runner = host.CreateRunnerWithTimeProvider(clock, static options =>
        {
            options.RunTimeout = Installation;
            options.MaxRunTimeout = TimeSpan.FromMinutes(20);
        });
        var collecting = Collect(runner, "chain");

        await started.Task.WaitAsync(TestContext.Current.CancellationToken);

        clock.Pending.ShouldBe([TimeSpan.FromMinutes(20)]);

        clock.Advance(TimeSpan.FromMinutes(20));

        (await RunOf(host, await collecting.WaitAsync(TestContext.Current.CancellationToken))).Status.ShouldBe(RunStatus.Canceled);
    }

    [Fact]
    public async Task The_definitions_run_timeout_also_bounds_the_wait_for_a_slot()
    {
        // One slot: the first run holds it; the second waits. The second's
        // definition allows thirty minutes and the installation ten: fifteen
        // minutes in the queue must not stop it.
        var clock = new ManualClock();
        var host = Host(clock);
        var holding = Signal();
        var release = Signal();

        host.AddFunction<List<ChatMessage>, List<ChatMessage>>(
            "hold",
            async (messages, _, _) =>
            {
                holding.TrySetResult();
                await release.Task.ConfigureAwait(false);

                return messages;
            });

        await SaveAsync(host, "first", Function("hold"));
        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "second",
            Kind = WorkflowKind.Sequential,
            Nodes = [Function("hold")],
            RunTimeout = TimeSpan.FromMinutes(30),
        });

        var runner = host.CreateRunnerWithTimeProvider(clock, static options =>
        {
            options.MaxConcurrentRuns = 1;
            options.RunTimeout = Installation;
            options.MaxRunTimeout = TimeSpan.FromHours(1);
        });

        var first = Collect(runner, "first");

        await holding.Task.WaitAsync(TestContext.Current.CancellationToken);

        var second = Collect(runner, "second");

        // The second run's deadline is moved to its own thirty minutes before it
        // waits for the slot.
        await WaitUntil.TrueAsync(
            () => clock.Pending.Contains(TimeSpan.FromMinutes(30)),
            "the waiting run's deadline to move to its definition's value");

        // Past the installation's ten minutes, still in the queue.
        clock.Advance(TimeSpan.FromMinutes(15));
        second.IsCompleted.ShouldBeFalse("the waiting run is bound by its definition's thirty minutes");

        // The first run's node ignores its token, so the first run ends only when it
        // returns - Canceled, its own ten minutes are gone - and frees the slot.
        release.TrySetResult();

        (await RunOf(host, await first.WaitAsync(TestContext.Current.CancellationToken))).Status.ShouldBe(RunStatus.Canceled);

        var secondRun = await RunOf(host, await second.WaitAsync(TestContext.Current.CancellationToken));

        secondRun.Status.ShouldBe(RunStatus.Completed);
    }

    private static async Task SaveRunTimeoutAsync(WorkflowTestHost host, TimeSpan runTimeout)
        => await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            Nodes = [Function("render")],
            RunTimeout = runTimeout,
        });
}
