using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:Scheduling</c> really reaches
/// <see cref="TraconSchedulingOptions"/> — the scalars and the five collections.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 F-385: the binder read only the seven scalar keys. <c>Lanes</c>,
/// <c>MaxConcurrentJobsPerLane</c>, <c>LaneByHandlerKey</c> and
/// <c>HttpSchedulableHandlerKeys</c> were listed as configuration keys on the
/// site and bound nowhere, so a worker's <c>media</c> lane budget in
/// <c>appsettings.json</c> was silently ignored and the worker leased every
/// lane. A consumer only saw it in production.
/// </para>
/// <para>
/// The section is bound by hand without reflection (K-021, AOT), the same shape
/// <see cref="TraconImageOptionsBindingTests"/> covers.
/// </para>
/// </remarks>
public sealed class TraconSchedulingOptionsBindingTests
{
    private static TraconSchedulingOptions Bind(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddTracon(configuration.GetSection("Tracon"));

        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<TraconSchedulingOptions>>().Value;
    }

    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        var options = Bind(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Scheduling:Enabled"] = "false",
            ["Tracon:Scheduling:RunWorker"] = "false",
            ["Tracon:Scheduling:MaxConcurrentJobs"] = "7",
            ["Tracon:Scheduling:PollInterval"] = "00:00:03",
            ["Tracon:Scheduling:LeaseDuration"] = "00:02:00",
            ["Tracon:Scheduling:MaxAttempts"] = "5",
            ["Tracon:Scheduling:MaxItemsPerJob"] = "250",
        });

        options.Enabled.ShouldBeFalse();
        options.RunWorker.ShouldBeFalse();
        options.MaxConcurrentJobs.ShouldBe(7);
        options.PollInterval.ShouldBe(TimeSpan.FromSeconds(3));
        options.LeaseDuration.ShouldBe(TimeSpan.FromMinutes(2));
        options.MaxAttempts.ShouldBe(5);
        options.MaxItemsPerJob.ShouldBe(250);
    }

    [Fact]
    public void The_lane_collections_are_bound()
    {
        var options = Bind(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Scheduling:Lanes:0"] = "media",
            ["Tracon:Scheduling:Lanes:1"] = JobLanes.Default,
            ["Tracon:Scheduling:MaxConcurrentJobsPerLane:media"] = "3",
            ["Tracon:Scheduling:LaneByHandlerKey:tracon.workflow"] = "media",
            ["Tracon:Scheduling:HttpSchedulableHandlerKeys:0"] = JobHandlerKeys.AgentBatch,
            ["Tracon:Scheduling:HttpSchedulableHandlerKeys:1"] = "contoso.nightly-report",
        });

        options.Lanes.ShouldNotBeNull().ShouldBe(["media", JobLanes.Default]);
        options.MaxConcurrentJobsPerLane["media"].ShouldBe(3);
        options.LaneByHandlerKey["tracon.workflow"].ShouldBe("media");
        options.HttpSchedulableHandlerKeys.ShouldBe([JobHandlerKeys.AgentBatch, "contoso.nightly-report"]);
    }

    [Fact]
    public void A_lane_name_is_bound_as_written_and_checked_at_startup()
    {
        // Lanes and handler keys are matched ordinally and a lane name is
        // lower case. A binder that lower-cased "Media" would hide the typo and
        // route the budget to a lane nobody queues under; it reaches the
        // validator as written instead.
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Scheduling:MaxConcurrentJobsPerLane:Media"] = "2",
        };

        var failure = Should.Throw<OptionsValidationException>(() => Bind(values));

        failure.Message.ShouldContain("'Media'", Case.Sensitive);
    }

    [Fact]
    public void A_lane_budget_that_is_not_a_number_fails_at_startup()
    {
        // A silently skipped budget is the defect this class closes, so an
        // unreadable value stops the host and is named with its key and its text.
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Scheduling:MaxConcurrentJobsPerLane:media"] = "three",
        };

        var failure = Should.Throw<OptionsValidationException>(() => Bind(values));

        failure.Message.ShouldContain("Tracon:Scheduling:MaxConcurrentJobsPerLane:media is 'three'");
    }

    [Fact]
    public void An_empty_lane_mapping_fails_at_startup()
    {
        // Skipping it would drop the mapping silently and route the key's jobs
        // to the default lane: the class of failure this binder closes.
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Scheduling:LaneByHandlerKey:tracon.workflow"] = "",
        };

        var failure = Should.Throw<OptionsValidationException>(() => Bind(values));

        failure.Message.ShouldContain(nameof(TraconSchedulingOptions.LaneByHandlerKey));
    }

    [Fact]
    public void The_max_duration_map_is_bound()
    {
        // F-323: the fifth collection. Bound by hand like the other four; a key
        // with dots is a handler key, kept as written.
        var options = Bind(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Scheduling:MaxDurationByHandlerKey:tracon.workflow"] = "00:30:00",
            ["Tracon:Scheduling:MaxDurationByHandlerKey:contoso.nightly-report"] = "01:00:00",
        });

        options.MaxDurationByHandlerKey[JobHandlerKeys.Workflow].ShouldBe(TimeSpan.FromMinutes(30));
        options.MaxDurationByHandlerKey["contoso.nightly-report"].ShouldBe(TimeSpan.FromHours(1));
    }

    [Fact]
    public void An_unreadable_max_duration_fails_at_startup()
    {
        // An unreadable duration is left out and named with its key and its text; a
        // skipped entry would leave the key's jobs without the limit the operator wrote.
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Scheduling:MaxDurationByHandlerKey:tracon.workflow"] = "half an hour",
        };

        var failure = Should.Throw<OptionsValidationException>(() => Bind(values));

        failure.Message.ShouldContain("Tracon:Scheduling:MaxDurationByHandlerKey:tracon.workflow is 'half an hour'");
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:00:05")]
    [InlineData("50.00:00:00")]
    public void A_max_duration_a_timer_cannot_arm_fails_at_startup(string value)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Scheduling:MaxDurationByHandlerKey:tracon.workflow"] = value,
        };

        var failure = Should.Throw<OptionsValidationException>(() => Bind(values));

        failure.Message.ShouldContain(nameof(TraconSchedulingOptions.MaxDurationByHandlerKey));
        failure.Message.ShouldContain("'tracon.workflow'");
    }

    [Fact]
    public void An_absent_section_leaves_the_shipped_defaults()
    {
        var options = Bind([]);

        options.Enabled.ShouldBeTrue();
        options.RunWorker.ShouldBeTrue();
        options.MaxConcurrentJobs.ShouldBe(2);
        options.Lanes.ShouldBeNull();
        options.MaxConcurrentJobsPerLane.ShouldBeEmpty();
        options.LaneByHandlerKey.ShouldBeEmpty();
        options.HttpSchedulableHandlerKeys.ShouldBeEmpty();
        options.MaxDurationByHandlerKey.ShouldBeEmpty();
    }
}
