namespace Tracon.Core.UnitTests.Graph;

/// <summary>
/// <see cref="TraconRunOptions.Clone"/> behavior.
/// </summary>
/// <remarks>
/// Learned: when a middleware layer that copies the settings dropped
/// the id, the run id reported to the client no longer matched any record.
/// The same trap is more insidious for the tree fields - a dropped <c>Depth</c>
/// value silently disables recursion protection.
/// </remarks>
public sealed class TraconRunOptionsTests
{
    [Fact]
    public void Clone_preserves_all_tree_fields()
    {
        var budget = new AgentRunBudget { MaxDepth = 2, MaxTotalTokens = 500 };

        var original = new TraconRunOptions
        {
            RunId = TraconId.NewId(),
            ParentRunId = TraconId.NewId(),
            RootRunId = TraconId.NewId(),
            Depth = 2,
            Budget = budget,
        };

        var clone = original.Clone().ShouldBeOfType<TraconRunOptions>();

        clone.RunId.ShouldBe(original.RunId);
        clone.ParentRunId.ShouldBe(original.ParentRunId);
        clone.RootRunId.ShouldBe(original.RootRunId);
        clone.Depth.ShouldBe(2);

        // The budget must be the SAME instance. Value equality is not enough:
        // a copied budget would give each branch its own limit.
        clone.Budget.ShouldBeSameAs(budget);
    }

    [Fact]
    public void Clone_preserves_the_culture()
    {
        // A middleware layer that copies the options and drops the culture hands the sub-agents of the run
        // the default instructions, with nothing to show for it.
        var clone = new TraconRunOptions { Culture = "tr-TR" }.Clone().ShouldBeOfType<TraconRunOptions>();

        clone.Culture.ShouldBe("tr-TR");
    }

    [Fact]
    public async Task Clone_preserves_the_session_the_files_of_the_run_belong_to()
    {
        // A copy that drops it leaves a sub-agent writing files under a session whose record nothing will write.
        var session = await new Microsoft.Agents.AI.ChatClientAgent(new Fakes.FakeChatClient()).CreateSessionAsync(TestContext.Current.CancellationToken);

        var clone = new TraconRunOptions { RecordSession = session }.Clone().ShouldBeOfType<TraconRunOptions>();

        clone.RecordSession.ShouldBeSameAs(session);
    }

    [Fact]
    public void Clone_leaves_empty_settings_empty()
    {
        var clone = new TraconRunOptions().Clone().ShouldBeOfType<TraconRunOptions>();

        clone.Culture.ShouldBeNull();

        clone.RunId.ShouldBeNull();
        clone.ParentRunId.ShouldBeNull();
        clone.RootRunId.ShouldBeNull();
        clone.Depth.ShouldBe(0);
        clone.Budget.ShouldBeNull();
    }
}
