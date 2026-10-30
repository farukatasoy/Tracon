namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:OnlineEvaluation</c> really reaches <see cref="OnlineEvaluationOptions"/>.
/// </summary>
/// <remarks>
/// The section is bound by hand (AOT): a key added to the options and not to
/// the binder is silently ignored. <see cref="OptionsKeyCoverage"/> fails this test
/// before such a key ships unbound.
/// </remarks>
public sealed class OnlineEvaluationOptionsBindingTests
{
    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        // "0.25" also proves the rate is read with the invariant culture.
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:OnlineEvaluation:Enabled"] = "true",
            ["Tracon:OnlineEvaluation:SampleRate"] = "0.25",
            ["Tracon:OnlineEvaluation:MaxScoresPerHour"] = "40",
            ["Tracon:OnlineEvaluation:LowScoreThreshold"] = "45",
            ["Tracon:OnlineEvaluation:MinSampleSize"] = "5",
            ["Tracon:OnlineEvaluation:JudgeTimeout"] = "00:00:45",
            ["Tracon:OnlineEvaluation:EvaluationWindow"] = "02:00:00",
        };

        var options = SectionBinding.Bind<OnlineEvaluationOptions>(values);

        options.Enabled.ShouldBeTrue();
        options.SampleRate.ShouldBe(0.25);
        options.MaxScoresPerHour.ShouldBe(40);
        options.LowScoreThreshold.ShouldBe(45);
        options.MinSampleSize.ShouldBe(5);
        options.JudgeTimeout.ShouldBe(TimeSpan.FromSeconds(45));
        options.EvaluationWindow.ShouldBe(TimeSpan.FromHours(2));
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:OnlineEvaluation", options, "AgentNames");
    }

    [Fact]
    public void The_agent_names_are_bound()
    {
        var options = SectionBinding.Bind<OnlineEvaluationOptions>(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:OnlineEvaluation:AgentNames:0"] = "support",
            ["Tracon:OnlineEvaluation:AgentNames:1"] = "billing",
        });

        options.AgentNames.ShouldBe(["support", "billing"]);
    }

    [Fact]
    public void An_absent_section_leaves_the_shipped_defaults()
    {
        var options = SectionBinding.Bind<OnlineEvaluationOptions>(SectionBinding.NoKeys);

        options.Enabled.ShouldBeFalse();
        options.SampleRate.ShouldBe(0);
        options.MaxScoresPerHour.ShouldBe(100);
        options.AgentNames.ShouldBeEmpty();
        options.LowScoreThreshold.ShouldBe(60);
        options.MinSampleSize.ShouldBe(20);
        options.JudgeTimeout.ShouldBe(TimeSpan.FromSeconds(60));
        options.EvaluationWindow.ShouldBe(TimeSpan.FromHours(1));
    }
}
