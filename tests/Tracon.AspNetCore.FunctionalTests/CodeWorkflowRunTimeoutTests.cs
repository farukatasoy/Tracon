using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Tests.Common;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.NodeUsageHost;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The run timeout of a workflow defined in code (<c>WorkflowRegistrationOptions.RunTimeout</c>): the same rule
/// a stored definition has. It shortens the installation's value, lengthens it up to <c>MaxRunTimeout</c>, and a run
/// that reaches it names the registration.
/// </summary>
/// <remarks>
/// A code workflow was tied to the installation's ten minutes: a pipeline that renders for an hour had to raise the
/// value for every workflow, and one that should give up in a minute could not say so.
/// </remarks>
public sealed class CodeWorkflowRunTimeoutTests
{
    private const string Flow = "slow-job";

    [Fact]
    public async Task A_run_that_reaches_the_registrations_run_timeout_ends_canceled_and_names_the_registration()
    {
        var clock = new ManualClock();

        await using var host = await StartAsync(
            clock,
            runTimeout: TimeSpan.FromMilliseconds(300),
            installation: TimeSpan.FromMinutes(10),
            ceiling: null,
            work: TimeSpan.FromMinutes(5));

        var run = await RecordAsync(host, await RunWithinAsync(host, clock, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1)));

        clock.Steps.ShouldBe([TimeSpan.FromMilliseconds(300)], "the run ended at its registration's timeout, not at the installation's or the node's");
        run.Status.ShouldBe(RunStatus.Canceled);

        var error = run.Error.ShouldNotBeNull();

        error.Type.ShouldBe(nameof(TimeoutException));
        error.Message.ShouldContain("00:00:00.3000000", Case.Sensitive);
        error.Message.ShouldContain("the run timeout its registration sets", Case.Sensitive);
        error.Message.ShouldContain("WorkflowRegistrationOptions.RunTimeout", Case.Sensitive);
    }

    [Fact]
    public async Task A_registration_may_lengthen_the_installations_run_timeout_up_to_the_ceiling()
    {
        // The installation would stop the run after 300 ms; the registration allows five seconds and the host's ceiling
        // allows an hour. The node takes 800 ms.
        var clock = new ManualClock();

        await using var host = await StartAsync(
            clock,
            runTimeout: TimeSpan.FromSeconds(5),
            installation: TimeSpan.FromMilliseconds(300),
            ceiling: TimeSpan.FromHours(1),
            work: TimeSpan.FromMilliseconds(800));

        var run = await RecordAsync(host, await RunWithinAsync(host, clock, TimeSpan.FromMilliseconds(800), TimeSpan.FromSeconds(1)));

        clock.Steps.ShouldBe([TimeSpan.FromMilliseconds(800)], "the installation's 300 ms was moved away before the clock reached it: the run waited for the node alone");
        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);
    }

    [Fact]
    public async Task A_registration_without_a_run_timeout_keeps_the_installations_value()
    {
        var clock = new ManualClock();

        await using var host = await StartAsync(
            clock,
            runTimeout: null,
            installation: TimeSpan.FromMilliseconds(300),
            ceiling: null,
            work: TimeSpan.FromMinutes(5));

        var run = await RecordAsync(host, await RunWithinAsync(host, clock, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1)));

        clock.Steps.ShouldBe([TimeSpan.FromMilliseconds(300)], "the run ended at the installation's timeout");
        run.Status.ShouldBe(RunStatus.Canceled);
        run.Error.ShouldNotBeNull().Message.ShouldContain("Tracon:Workflows:RunTimeout", Case.Sensitive);
    }

    [Fact]
    public async Task A_run_timeout_above_the_ceiling_stops_the_host_when_it_starts()
    {
        var problem = await Should.ThrowAsync<Exception>(async () =>
        {
            await using var host = await StartAsync(
                new ManualClock(),
                runTimeout: TimeSpan.FromHours(2),
                installation: TimeSpan.FromMinutes(10),
                ceiling: null,
                work: TimeSpan.Zero);
        });

        problem.Message.ShouldContain($"Workflow '{Flow}'", Case.Sensitive);
        problem.Message.ShouldContain("Tracon:Workflows:MaxRunTimeout", Case.Sensitive);
    }

    /// <summary>
    /// Runs the workflow and moves the host's clock to the timers that fall within <paramref name="within"/>, the run's
    /// timeout and the node's work among them, and no further.
    /// </summary>
    /// <remarks>
    /// A run is armed with the installation's value when it starts and moved to the registration's once the definition is
    /// read, which is before the node starts. A clock moved to the first timer it sees could fire the installation's
    /// shorter one in between, so the clock waits until the node's work (<paramref name="work"/>) is on it.
    /// </remarks>
    private static async Task<Guid> RunWithinAsync(TraconTestHost host, ManualClock clock, TimeSpan work, TimeSpan within)
    {
        var running = RunAsync(host, Flow);

        await WaitUntil.TrueAsync(
            () => running.IsCompleted || clock.Pending.Contains(work),
            "the node's work to wait on the clock, after the run moved its deadline to the registration's");

        await clock.AdvanceUntilAsync(running, within);

        return await running;
    }

    /// <summary>The node's work: it takes <paramref name="span"/> of the host's clock, and ends early when the run is cancelled.</summary>
    private static async Task PassAsync(TimeProvider clock, TimeSpan span, CancellationToken cancellationToken)
    {
        var passed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var timer = clock.CreateTimer(static state => ((TaskCompletionSource)state!).TrySetResult(), passed, span, Timeout.InfiniteTimeSpan);

        await passed.Task.WaitAsync(cancellationToken);
    }

    private static Task<TraconTestHost> StartAsync(ManualClock clock, TimeSpan? runTimeout, TimeSpan installation, TimeSpan? ceiling, TimeSpan work)
        => NodeUsageHost.StartAsync(
            builder =>
            {
                builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "render",
                    _ => async (messages, _, token) =>
                    {
                        await PassAsync(clock, work, token);

                        return messages;
                    });

                builder.AddWorkflow(
                    Flow,
                    static services =>
                    {
                        var render = services.GetWorkflowFunction("render");

                        return new WorkflowBuilder(render).WithOutputFrom(render).WithName(Flow).Build();
                    },
                    options => options.RunTimeout = runTimeout);
            },
            services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.Configure<TraconWorkflowOptions>(options =>
                {
                    options.RunTimeout = installation;
                    options.MaxRunTimeout = ceiling;
                });
            });
}
