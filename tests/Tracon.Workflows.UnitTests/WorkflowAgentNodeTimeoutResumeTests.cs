using System.Reflection;
using Tracon.Workflows.UnitTests.Fakes;
using static Tracon.Workflows.UnitTests.Fakes.TimeoutRuns;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// An agent node's timeout (F-405, failure modes 24 and 25): it becomes the step's own
/// deadline with its hard cutoff 30 seconds later, and changing it does not change the
/// step's executor identity.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ChildAgentInvoker"/> takes its limits in its constructor, so the wrapper
/// cache has to hold one wrapper per timeout. Microsoft Agent Framework derives the
/// executor id from the agent INSTANCE; every wrapper of a (workflow, agent) pair gets
/// the same id written onto it (<c>WorkflowAgentIdentity</c>), so a new instance must
/// still resume an old checkpoint. Measured here, not assumed.
/// </para>
/// <para>
/// The behavior against a model that does not answer (the step fails at the node's
/// timeout, not at the installation's two minutes) is <c>WorkflowAgentNodeTimeoutTests</c>
/// in the functional tests.
/// </para>
/// </remarks>
public sealed class WorkflowAgentNodeTimeoutResumeTests
{
    [Fact]
    public async Task Changing_an_agent_nodes_timeout_leaves_the_old_checkpoint_usable()
    {
        var clock = new ManualClock();
        var host = Host(clock, logs: null, "one", "two");

        await SaveAsync(host, "chain", Agent("one"), Agent("two"));

        var source = await RunOf(host, await Collect(host.CreateRunnerWithTimeProvider(clock), "chain"));

        source.Status.ShouldBe(RunStatus.Completed);

        var checkpoint = (await host.CheckpointStore.ListByRunAsync(host.TenantContext.TenantId, source.Id))[0];

        await SaveAsync(host, "chain", Agent("one", timeout: TimeSpan.FromMinutes(5)), Agent("two", timeout: TimeSpan.FromSeconds(90)));

        var resumed = await RunOf(
            host,
            await WorkflowFunctionTimeoutResumeTests.ResumeAsync(host, clock, source.Id, checkpoint.CheckpointId));

        resumed.Error.ShouldBeNull();
        resumed.Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public void A_wrapper_per_timeout_shares_the_executor_identity_of_its_pair()
    {
        var host = new WorkflowTestHost("one");

        var installation = host.AgentCache.Get("chain", "one", description: null);
        var bounded = host.AgentCache.Get("chain", "one", description: null, TimeSpan.FromMinutes(5));
        var again = host.AgentCache.Get("chain", "one", description: null, TimeSpan.FromMinutes(5));

        bounded.ShouldNotBeSameAs(installation, "the limits are constructor state, so a new timeout needs its own wrapper");
        again.ShouldBeSameAs(bounded, "the same timeout reuses its wrapper");
        bounded.Id.ShouldBe(installation.Id, "the executor id comes from the (workflow, agent) pair, never the timeout");
    }

    [Fact]
    public void The_nodes_timeout_becomes_the_steps_deadline_and_its_hard_cutoff_is_30_seconds_later()
    {
        var host = new WorkflowTestHost("one");
        var installation = new TraconOptions().AgentGraph;

        var bounded = host.AgentCache.Get("chain", "one", description: null, TimeSpan.FromMinutes(7));
        var plain = host.AgentCache.Get("chain", "one", description: null);

        Limits(bounded).ShouldBe((TimeSpan.FromMinutes(7), TimeSpan.FromMinutes(7) + TimeSpan.FromSeconds(30)));
        Limits(plain).ShouldBe((installation.ChildDeadline, installation.WaitTimeout));
    }

    private static (TimeSpan ChildDeadline, TimeSpan WaitTimeout) Limits(ChildAgentInvoker invoker)
    {
        static TimeSpan Read(ChildAgentInvoker target, string field)
            => (TimeSpan)typeof(ChildAgentInvoker)
                .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(target)!;

        return (Read(invoker, "_childDeadline"), Read(invoker, "_waitTimeout"));
    }
}
