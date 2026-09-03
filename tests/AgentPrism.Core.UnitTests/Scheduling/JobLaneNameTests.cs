namespace AgentPrism.Core.UnitTests.Scheduling;

/// <summary>Tests for <see cref="JobLanes"/> and the lane checks in <see cref="AgentPrismSchedulingOptionsValidator"/>.</summary>
public sealed class JobLaneNameTests
{
    private readonly AgentPrismSchedulingOptionsValidator _validator = new();

    [Theory]
    [InlineData("default")]
    [InlineData("media")]
    [InlineData("a")]
    [InlineData("media.retries")]
    [InlineData("media_retries")]
    [InlineData("media-retries")]
    [InlineData("9lives")]
    public void Valid_names_pass(string name) => JobLanes.IsValidName(name).ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Media")]
    [InlineData("MEDIA")]
    [InlineData("-media")]
    [InlineData(".media")]
    [InlineData("me dia")]
    [InlineData("me/dia")]
    public void Invalid_names_fail(string? name) => JobLanes.IsValidName(name).ShouldBeFalse();

    [Fact]
    public void A_65_character_name_is_rejected()
        => JobLanes.IsValidName(new string('a', 65)).ShouldBeFalse();

    [Fact]
    public void A_64_character_name_is_accepted()
        => JobLanes.IsValidName(new string('a', 64)).ShouldBeTrue();

    [Fact]
    public void Resolve_keeps_an_explicit_non_default_lane_even_if_lane_by_handler_key_maps_the_key()
    {
        var laneByHandlerKey = new Dictionary<string, string>(StringComparer.Ordinal) { [JobHandlerKeys.Retention] = "housekeeping" };

        JobLanes.Resolve("media", JobHandlerKeys.Retention, laneByHandlerKey).ShouldBe("media");
    }

    [Fact]
    public void Resolve_applies_lane_by_handler_key_only_when_the_lane_is_still_default()
    {
        var laneByHandlerKey = new Dictionary<string, string>(StringComparer.Ordinal) { [JobHandlerKeys.Retention] = "housekeeping" };

        JobLanes.Resolve(JobLanes.Default, JobHandlerKeys.Retention, laneByHandlerKey).ShouldBe("housekeeping");
    }

    [Fact]
    public void Resolve_leaves_default_alone_when_lane_by_handler_key_has_no_entry_for_the_key()
    {
        var laneByHandlerKey = new Dictionary<string, string>(StringComparer.Ordinal) { [JobHandlerKeys.Retention] = "housekeeping" };

        JobLanes.Resolve(JobLanes.Default, JobHandlerKeys.Eval, laneByHandlerKey).ShouldBe(JobLanes.Default);
    }

    [Fact]
    public void Resolve_throws_for_a_name_that_fails_validation()
        => Should.Throw<ArgumentException>(() => JobLanes.Resolve("Media", JobHandlerKeys.AgentBatch, laneByHandlerKey: null));

    [Fact]
    public void Resolve_throws_when_lane_by_handler_key_itself_maps_to_an_invalid_name()
    {
        var laneByHandlerKey = new Dictionary<string, string>(StringComparer.Ordinal) { [JobHandlerKeys.Retention] = "Housekeeping" };

        Should.Throw<ArgumentException>(() => JobLanes.Resolve(JobLanes.Default, JobHandlerKeys.Retention, laneByHandlerKey));
    }

    [Fact]
    public void Validator_accepts_default_options()
        => _validator.Validate(null, new AgentPrismSchedulingOptions()).Succeeded.ShouldBeTrue();

    [Fact]
    public void Validator_rejects_an_invalid_name_in_lanes()
    {
        var options = new AgentPrismSchedulingOptions { Lanes = ["Media"] };

        var result = _validator.Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(failure => failure.Contains("Lanes") && failure.Contains("Media"));
    }

    [Fact]
    public void Validator_rejects_an_invalid_name_in_max_concurrent_jobs_per_lane()
    {
        var options = new AgentPrismSchedulingOptions();
        options.MaxConcurrentJobsPerLane["Media"] = 1;

        var result = _validator.Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(failure => failure.Contains("MaxConcurrentJobsPerLane") && failure.Contains("Media"));
    }

    [Fact]
    public void Validator_rejects_a_non_positive_max_concurrent_jobs_per_lane_value()
    {
        var options = new AgentPrismSchedulingOptions();
        options.MaxConcurrentJobsPerLane["media"] = 0;

        var result = _validator.Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(failure => failure.Contains("MaxConcurrentJobsPerLane"));
    }

    [Fact]
    public void Validator_accepts_a_max_concurrent_jobs_per_lane_sum_that_exceeds_max_concurrent_jobs()
    {
        var options = new AgentPrismSchedulingOptions { MaxConcurrentJobs = 2 };
        options.MaxConcurrentJobsPerLane["media"] = 5;
        options.MaxConcurrentJobsPerLane["retention"] = 5;

        _validator.Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validator_rejects_an_invalid_name_in_lane_by_handler_key()
    {
        var options = new AgentPrismSchedulingOptions();
        options.LaneByHandlerKey[JobHandlerKeys.Retention] = "Housekeeping";

        var result = _validator.Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(failure => failure.Contains("LaneByHandlerKey") && failure.Contains("Housekeeping"));
    }
}
