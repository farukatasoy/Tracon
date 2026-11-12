namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:Workflows</c> reaches <see cref="TraconWorkflowOptions"/>.
/// </summary>
/// <remarks>
/// <para>
/// The values differ from the defaults for every member, and that combination is not a valid setup: resuming interrupted runs
/// needs checkpointing, and one value turns checkpointing off. The test is about the reading, not the rule, so the options
/// are read without the validators. The reading is also proved in
/// Tracon.Workflows.UnitTests with a sample per member found by reflection; this one fails on a member no key names.
/// </para>
/// </remarks>
public sealed class TraconWorkflowOptionsBindingTests
{
    [Fact]
    public void Every_member_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Workflows:Enabled"] = "false",
            ["Tracon:Workflows:EnableCheckpointing"] = "false",
            ["Tracon:Workflows:MaxConcurrentRuns"] = "7",
            ["Tracon:Workflows:MaxConcurrentRunsPerTenant"] = "3",
            ["Tracon:Workflows:RunTimeout"] = "00:05:00",
            ["Tracon:Workflows:MaxRunTimeout"] = "1.00:00:00",
            ["Tracon:Workflows:MaxSuperSteps"] = "11",
            ["Tracon:Workflows:MaxNodeSettingsLength"] = "2048",
            ["Tracon:Workflows:KeepCheckpointsAfterCompletion"] = "false",
            ["Tracon:Workflows:ResumeInterruptedRuns"] = "true",
            ["Tracon:Workflows:MaxResumeAttempts"] = "4",
            ["Tracon:Workflows:RequestExpirationScanInterval"] = "00:02:30",
            ["Tracon:Workflows:MaxRequestExpirationsPerScan"] = "17",
        };

        var options = SectionBinding.BindWithoutValidation<TraconWorkflowOptions>(
            values, static (builder, _) => builder.UseWorkflows());

        options.Enabled.ShouldBeFalse();
        options.EnableCheckpointing.ShouldBeFalse();
        options.MaxConcurrentRuns.ShouldBe(7);
        options.MaxConcurrentRunsPerTenant.ShouldBe(3);
        options.RunTimeout.ShouldBe(TimeSpan.FromMinutes(5));
        options.MaxRunTimeout.ShouldBe(TimeSpan.FromDays(1));
        options.MaxSuperSteps.ShouldBe(11);
        options.MaxNodeSettingsLength.ShouldBe(2048);
        options.KeepCheckpointsAfterCompletion.ShouldBeFalse();
        options.ResumeInterruptedRuns.ShouldBeTrue();
        options.MaxResumeAttempts.ShouldBe(4);
        options.RequestExpirationScanInterval.ShouldBe(TimeSpan.FromSeconds(150));
        options.MaxRequestExpirationsPerScan.ShouldBe(17);
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:Workflows", options);
    }
}
