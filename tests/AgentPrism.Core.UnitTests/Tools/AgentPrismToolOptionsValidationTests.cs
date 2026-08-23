namespace AgentPrism.Core.UnitTests.Tools;

/// <summary>Verifies fail-fast validation for <see cref="AgentPrismToolOptions"/>.</summary>
public sealed class AgentPrismToolOptionsValidationTests
{
    [Fact]
    public void Default_max_output_bytes_below_the_minimum_envelope_size_is_rejected()
    {
        var options = new AgentPrismOptions();
        options.Tools.DefaultMaxOutputBytes = TruncatingAIFunction.MinimumEnvelopeBytes - 1;

        var result = new AgentPrismOptionsValidator().Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(
            failure => failure.Contains(nameof(AgentPrismToolOptions.DefaultMaxOutputBytes), StringComparison.Ordinal));
    }

    [Fact]
    public void Default_max_output_bytes_at_the_minimum_envelope_size_is_accepted()
    {
        var options = new AgentPrismOptions();
        options.Tools.DefaultMaxOutputBytes = TruncatingAIFunction.MinimumEnvelopeBytes;

        var result = new AgentPrismOptionsValidator().Validate(null, options);

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Unset_default_max_output_bytes_is_accepted()
    {
        var options = new AgentPrismOptions();

        var result = new AgentPrismOptionsValidator().Validate(null, options);

        result.Succeeded.ShouldBeTrue();
    }
}
