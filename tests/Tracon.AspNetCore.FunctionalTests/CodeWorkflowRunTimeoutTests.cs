using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.NodeUsageHost;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The run timeout of a workflow defined in code (phase 211, <c>WorkflowRegistrationOptions.RunTimeout</c>): the same rule
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
        await using var host = await StartAsync(
            runTimeout: TimeSpan.FromMilliseconds(300),
            installation: TimeSpan.FromMinutes(10),
            ceiling: null,
            work: TimeSpan.FromMinutes(5));

        var run = await RecordAsync(host, await RunAsync(host, Flow));

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
        await using var host = await StartAsync(
            runTimeout: TimeSpan.FromSeconds(5),
            installation: TimeSpan.FromMilliseconds(300),
            ceiling: TimeSpan.FromHours(1),
            work: TimeSpan.FromMilliseconds(800));

        var run = await RecordAsync(host, await RunAsync(host, Flow));

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);
    }

    [Fact]
    public async Task A_registration_without_a_run_timeout_keeps_the_installations_value()
    {
        await using var host = await StartAsync(
            runTimeout: null,
            installation: TimeSpan.FromMilliseconds(300),
            ceiling: null,
            work: TimeSpan.FromMinutes(5));

        var run = await RecordAsync(host, await RunAsync(host, Flow));

        run.Status.ShouldBe(RunStatus.Canceled);
        run.Error.ShouldNotBeNull().Message.ShouldContain("Tracon:Workflows:RunTimeout", Case.Sensitive);
    }

    [Fact]
    public async Task A_run_timeout_above_the_ceiling_stops_the_host_when_it_starts()
    {
        var problem = await Should.ThrowAsync<Exception>(async () =>
        {
            await using var host = await StartAsync(
                runTimeout: TimeSpan.FromHours(2),
                installation: TimeSpan.FromMinutes(10),
                ceiling: null,
                work: TimeSpan.Zero);
        });

        problem.Message.ShouldContain($"Workflow '{Flow}'", Case.Sensitive);
        problem.Message.ShouldContain("Tracon:Workflows:MaxRunTimeout", Case.Sensitive);
    }

    private static Task<TraconTestHost> StartAsync(TimeSpan? runTimeout, TimeSpan installation, TimeSpan? ceiling, TimeSpan work)
        => NodeUsageHost.StartAsync(
            builder =>
            {
                builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "render",
                    _ => async (messages, _, token) =>
                    {
                        await Task.Delay(work, token); // delay: simulated

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
            services => services.Configure<TraconWorkflowOptions>(options =>
            {
                options.RunTimeout = installation;
                options.MaxRunTimeout = ceiling;
            }));
}
