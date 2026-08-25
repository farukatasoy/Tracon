namespace AgentPrism.Core.UnitTests.Evaluation;

/// <summary>Tests for startup validation of registered run judges.</summary>
public sealed class RunJudgeSetTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not valid")]
    public void Invalid_names_are_rejected_at_startup(string name)
        => Should.Throw<AgentPrismException>(() => new RunJudgeSet([new FixedJudge(name)]));

    [Fact]
    public void Names_are_unique_without_case_sensitivity()
        => Should.Throw<AgentPrismException>(() => new RunJudgeSet([new FixedJudge("quality"), new FixedJudge("QUALITY")]));

    private sealed class FixedJudge(string name) : IRunJudge
    {
        public string Name => name;

        public ValueTask<RunJudgment> JudgeAsync(RunJudgeContext context, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new RunJudgment());
    }
}
