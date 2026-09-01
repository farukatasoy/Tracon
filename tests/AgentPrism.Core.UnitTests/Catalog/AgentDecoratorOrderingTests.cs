using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.Core.UnitTests.Catalog;

/// <summary>
/// Locks the direction <see cref="IAgentDecorator.Order"/> applies in, and keeps
/// the interface's own documentation pointing the same way.
/// </summary>
/// <remarks>
/// <para>
/// Nothing measured this before (BL-034). The pipeline sorts with
/// <c>OrderByDescending(d =&gt; d.Order)</c> and each turn wraps the agent the
/// previous turn produced, so the HIGHEST order is applied FIRST and ends up
/// INNERMOST, and the lowest order ends up outermost. The XML documentation on
/// <c>Order</c> stated the opposite, while the example sentence right after it
/// ("Run recording uses 0, which makes it the outermost decorator") stated the
/// truth - the interface contradicted itself, and a consumer who read the rule
/// instead of the example would place a security decorator on the wrong layer.
/// </para>
/// <para>
/// These two tests lock the BEHAVIOR. The wrong sentence itself is caught by
/// <c>OrderingContractDocumentationTests</c>, which reads the interface source -
/// no behavior test can see a sentence, and the sentence is the only contract a
/// packaged consumer gets.
/// </para>
/// </remarks>
public sealed class AgentDecoratorOrderingTests
{
    [Fact]
    public void A_lower_order_decorator_wraps_outside_a_higher_order_one()
    {
        var root = new FakeChatClient().AsAIAgent();

        // Registered deliberately out of order: the pipeline, not the caller, decides.
        var decorated = AgentDecoratorPipeline.Apply(
            root,
            Descriptor(),
            [new LabelDecorator("middle", 10), new LabelDecorator("inner", 20), new LabelDecorator("outer", 0)]);

        Layers(decorated).ShouldBe(["outer", "middle", "inner"]);
    }

    [Fact]
    public void The_registered_decorators_put_run_recording_outside_the_tool_approval_gate()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism();

        using var provider = services.BuildServiceProvider();

        var applied = provider.GetServices<IAgentDecorator>()
            .OrderByDescending(static decorator => decorator.Order)
            .Select(static decorator => decorator.GetType().Name)
            .ToArray();

        // Applied first == innermost, so the outermost is the LAST entry.
        var outermost = applied[^1];
        var innermost = applied[0];

        outermost.ShouldBe(
            nameof(RunRecordingAgentDecorator),
            "run recording has to wrap everything, otherwise a run the tool-approval gate "
            + "rejects is never recorded");

        innermost.ShouldBe(
            nameof(StructuredResponseValidatingAgentDecorator),
            "structured response validation (docs/131, Order 30) has to sit closest to the compiled agent, "
            + "so a rejection is captured by every outer decorator (telemetry, run recording) exactly like "
            + "any other run-ending error");
    }

    private static AgentDescriptor Descriptor() => new()
    {
        Name = "ordering-probe",
        Origin = AgentDefinitionOrigin.Code,
        SourceName = "test",
    };

    /// <summary>Walks the wrapper chain from the outermost layer inwards.</summary>
    private static List<string> Layers(AIAgent agent)
    {
        var labels = new List<string>();

        while (agent is LabelAgent layer)
        {
            labels.Add(layer.Label);
            agent = layer.Inner;
        }

        return labels;
    }

    private sealed class LabelDecorator(string label, int order) : IAgentDecorator
    {
        public int Order => order;

        public AIAgent Decorate(AIAgent agent, AgentDescriptor descriptor) => new LabelAgent(label, agent);
    }

    private sealed class LabelAgent : DelegatingAIAgent
    {
        public LabelAgent(string label, AIAgent inner)
            : base(inner)
        {
            Label = label;
            Inner = inner;
        }

        public string Label { get; }

        public AIAgent Inner { get; }
    }
}
