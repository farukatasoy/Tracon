namespace Tracon.Core.UnitTests.Tools;

/// <summary>Verifies fail-fast validation for <see cref="TraconToolOptions"/>.</summary>
public sealed class TraconToolOptionsValidationTests
{
    [Fact]
    public void Default_max_output_bytes_below_the_minimum_envelope_size_is_rejected()
    {
        var options = new TraconOptions();
        options.Tools.DefaultMaxOutputBytes = TruncatingAIFunction.MinimumEnvelopeBytes - 1;

        var result = new TraconOptionsValidator().Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(
            failure => failure.Contains(nameof(TraconToolOptions.DefaultMaxOutputBytes), StringComparison.Ordinal));
    }

    [Fact]
    public void Default_max_output_bytes_at_the_minimum_envelope_size_is_accepted()
    {
        var options = new TraconOptions();
        options.Tools.DefaultMaxOutputBytes = TruncatingAIFunction.MinimumEnvelopeBytes;

        var result = new TraconOptionsValidator().Validate(null, options);

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Default_max_output_bytes_left_at_its_default_is_accepted()
    {
        var options = new TraconOptions();

        var result = new TraconOptionsValidator().Validate(null, options);

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void A_tool_result_is_bounded_at_one_mebibyte_unless_the_installation_says_otherwise()
    {
        // A result reaches the model, the run record and the session in full: 20 MB from a tool server cost 1.6 GB.
        new TraconOptions().Tools.DefaultMaxOutputBytes.ShouldBe(1024 * 1024);
    }

    [Fact]
    public void Unlimited_output_is_chosen_with_null_and_is_accepted()
    {
        var options = new TraconOptions();
        options.Tools.DefaultMaxOutputBytes = null;

        var result = new TraconOptionsValidator().Validate(null, options);

        result.Succeeded.ShouldBeTrue();
    }
}
