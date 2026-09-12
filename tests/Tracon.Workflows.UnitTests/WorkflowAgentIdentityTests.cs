using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// Stability of executor ids across the life of a process.
/// </summary>
/// <remarks>
/// 🚨 This test exists because of a measured limitation. Microsoft Agent Framework
/// derives executor ids from the agent <em>instance</em>, and <c>AIAgent.Id</c>
/// is generated randomly for every instance; in Phase 15, restarting the app made
/// old checkpoints unusable. Phase 16 derives the id from the
/// <c>(workflow, agent)</c> pair instead. The write path for the id relies on a
/// private field of MAF - if MAF removes that field, <strong>this test breaks</strong>
/// and the limitation does not silently return.
/// </remarks>
public sealed class WorkflowAgentIdentityTests
{
    [Fact]
    public void Persistent_id_can_be_written()
    {
        // If this goes red: MAF has changed its AIAgent.Id implementation.
        // Checkpoints become unusable after a restart.
        WorkflowAgentIdentity.IsSupported.ShouldBeTrue(
            "Microsoft Agent Framework may have changed the 'AIAgent.Id' field; " +
            "the persistent executor id is written to that field.");
    }

    [Fact]
    public void Same_pair_produces_the_same_id()
    {
        var first = WorkflowAgentIdentity.Compute("chain", "summarizer");
        var second = WorkflowAgentIdentity.Compute("chain", "summarizer");

        first.ShouldBe(second);

        // The format must match the id MAF produces: the executor id is joined
        // as '{name}_{id}' and must not carry a separator character.
        first.Length.ShouldBe(32);
        first.ShouldAllBe(character => Uri.IsHexDigit(character));
    }

    [Fact]
    public void Different_workflow_or_agent_produces_a_different_id()
    {
        var baseline = WorkflowAgentIdentity.Compute("chain", "summarizer");

        Differs(WorkflowAgentIdentity.Compute("chain", "translator"), baseline);
        Differs(WorkflowAgentIdentity.Compute("other-chain", "summarizer"), baseline);

        // '\n' was chosen as the separator character: ("a-b","c") and ("a","b-c")
        // cannot produce the same id.
        Differs(WorkflowAgentIdentity.Compute("a-b", "c"), WorkflowAgentIdentity.Compute("a", "b-c"));
    }

    private static void Differs(string actual, string other)
        => string.Equals(actual, other, StringComparison.Ordinal)
            .ShouldBeFalse($"'{actual}' and '{other}' should not have been the same id.");

    [Fact]
    public void Wrapper_carries_the_persistent_id()
    {
        var host = new WorkflowTestHost("summarizer");

        var agent = host.AgentCache.Get("chain", "summarizer", description: null);

        agent.Id.ShouldBe(WorkflowAgentIdentity.Compute("chain", "summarizer"));
    }

    [Fact]
    public void A_freshly_rebuilt_cache_produces_the_SAME_id()
    {
        // The unit-test equivalent of a process restart: the cache is rebuilt
        // from scratch. In Phase 15 this changed the ids, and a pending human
        // request got lost on every deployment.
        var host = new WorkflowTestHost("summarizer");

        var first = host.AgentCache.Get("chain", "summarizer", description: null).Id;

        var restarted = new WorkflowAgentCache(
            host.Resolver, host.TenantContext, NullLoggerFactory.Instance, Options.Create(new TraconOptions()));
        var second = restarted.Get("chain", "summarizer", description: null).Id;

        second.ShouldBe(first);
    }
}
