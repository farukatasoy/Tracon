namespace AgentPrism.Core.UnitTests.Graph;

/// <summary>
/// <see cref="AgentPrismRunOptions.Clone"/> behavior.
/// </summary>
/// <remarks>
/// Learned in K-044: when a middleware layer that copies the settings dropped
/// the id, the run id reported to the client no longer matched any record.
/// The same trap is more insidious for the tree fields - a dropped <c>Depth</c>
/// value silently disables recursion protection.
/// </remarks>
public sealed class AgentPrismRunOptionsTests
{
    [Fact]
    public void Clone_preserves_all_tree_fields()
    {
        var budget = new AgentRunBudget { MaxDepth = 2, MaxTotalTokens = 500 };

        var original = new AgentPrismRunOptions
        {
            RunId = AgentPrismId.NewId(),
            ParentRunId = AgentPrismId.NewId(),
            RootRunId = AgentPrismId.NewId(),
            Depth = 2,
            Budget = budget,
        };

        var clone = original.Clone().ShouldBeOfType<AgentPrismRunOptions>();

        clone.RunId.ShouldBe(original.RunId);
        clone.ParentRunId.ShouldBe(original.ParentRunId);
        clone.RootRunId.ShouldBe(original.RootRunId);
        clone.Depth.ShouldBe(2);

        // The budget must be the SAME instance. Value equality is not enough:
        // a copied budget would give each branch its own limit.
        clone.Budget.ShouldBeSameAs(budget);
    }

    [Fact]
    public void Clone_leaves_empty_settings_empty()
    {
        var clone = new AgentPrismRunOptions().Clone().ShouldBeOfType<AgentPrismRunOptions>();

        clone.RunId.ShouldBeNull();
        clone.ParentRunId.ShouldBeNull();
        clone.RootRunId.ShouldBeNull();
        clone.Depth.ShouldBe(0);
        clone.Budget.ShouldBeNull();
    }
}
