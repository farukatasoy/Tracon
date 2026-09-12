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
    public void Unset_default_max_output_bytes_is_accepted()
    {
        var options = new TraconOptions();

        var result = new TraconOptionsValidator().Validate(null, options);

        result.Succeeded.ShouldBeTrue();
    }
}
