namespace Tracon.Core.UnitTests.Storage;

/// <summary>
/// The invariants <see cref="RunScoreRules"/> holds every store to (phase 152).
/// </summary>
/// <remarks>
/// The rule is public because <c>IRunScoreStore</c> is an extension point: a
/// consumer's own store enforces the same invariants, and the shipped
/// <c>RunScoreStoreContract</c> holds it to them.
/// </remarks>
public sealed class RunScoreValidationTests
{
    [Theory]
    [InlineData("overall")]
    [InlineData("helpfulness")]
    [InlineData("a.b-c_d")]
    [InlineData("A")]
    [InlineData("0")]
    public void A_legal_name_is_accepted(string name)
        => RunScoreRules.IsValidName(name).ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("has space")]
    [InlineData("naïve")]
    [InlineData("judge:quality")]
    [InlineData("slash/name")]
    public void An_illegal_name_is_rejected(string? name)
        => RunScoreRules.IsValidName(name).ShouldBeFalse();

    [Fact]
    public void The_name_limit_is_64_characters()
    {
        RunScoreRules.IsValidName(new string('a', RunScoreRules.MaxNameLength)).ShouldBeTrue();
        RunScoreRules.IsValidName(new string('a', RunScoreRules.MaxNameLength + 1)).ShouldBeFalse();
    }

    /// <remarks>
    /// The judge name rule and the score name rule are deliberately the SAME
    /// expression: a judge writes its own name into the score, so two rules
    /// would mean a judge the host accepted could not write a score.
    /// </remarks>
    [Fact]
    public void The_name_rule_matches_the_judge_name_rule()
    {
        Should.NotThrow(() => new RunJudgeSet([new NamedJudge("a.b-c_d")]));
        RunScoreRules.IsValidName("a.b-c_d").ShouldBeTrue();

        Should.Throw<TraconException>(() => new RunJudgeSet([new NamedJudge("a:b")]));
        RunScoreRules.IsValidName("a:b").ShouldBeFalse();
    }

    [Fact]
    public void A_categorical_score_needs_a_text_value()
        => Should.Throw<ArgumentException>(
            () => RunScoreRules.Validate(Score with { Kind = RunScoreKind.Categorical, Value = null }));

    [Fact]
    public void A_categorical_score_carrying_a_numeric_value_is_rejected()
        => Should.Throw<ArgumentException>(() => RunScoreRules.Validate(
            Score with { Kind = RunScoreKind.Categorical, Value = 1, TextValue = "minor" }));

    [Fact]
    public void A_non_categorical_score_carrying_a_text_value_is_rejected()
        => Should.Throw<ArgumentException>(() => RunScoreRules.Validate(
            Score with { Kind = RunScoreKind.Numeric, Value = 1, TextValue = "minor" }));

    [Fact]
    public void A_text_value_longer_than_the_limit_is_rejected()
        => Should.Throw<ArgumentException>(() => RunScoreRules.Validate(Score with
        {
            Kind = RunScoreKind.Categorical,
            Value = null,
            TextValue = new string('a', RunScoreRules.MaxTextValueLength + 1),
        }));

    [Fact]
    public void A_text_value_at_the_limit_is_accepted()
        => Should.NotThrow(() => RunScoreRules.Validate(Score with
        {
            Kind = RunScoreKind.Categorical,
            Value = null,
            TextValue = new string('a', RunScoreRules.MaxTextValueLength),
        }));

    /// <remarks>
    /// A null value is NOT an invariant violation on a numeric kind: it records
    /// that no measurement was made, the same rule <c>JudgeScore.Value</c>
    /// already states. Only the categorical/numeric MIX is refused.
    /// </remarks>
    [Fact]
    public void A_numeric_score_with_no_measurement_is_accepted()
        => Should.NotThrow(() => RunScoreRules.Validate(Score with { Kind = RunScoreKind.Numeric, Value = null }));

    [Fact]
    public void A_null_score_is_rejected()
        => Should.Throw<ArgumentNullException>(() => RunScoreRules.Validate(null!));

    private static RunScore Score { get; } = new()
    {
        TenantId = "test",
        RunId = TraconId.NewId(),
        Name = "helpfulness",
        Kind = RunScoreKind.Binary,
        Value = 1,
        Source = "human",
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    private sealed class NamedJudge(string name) : IRunJudge
    {
        public string Name => name;

        public ValueTask<RunJudgment> JudgeAsync(RunJudgeContext context, CancellationToken cancellationToken = default)
            => new(JudgeVerdict.Headline(name, 1));
    }
}
