namespace AgentPrism.Core.UnitTests.Compilation;

/// <summary>Tests for <see cref="AgentPrismStructuredResponseOptionsValidator"/>.</summary>
public sealed class AgentPrismStructuredResponseOptionsValidatorTests
{
    private readonly AgentPrismStructuredResponseOptionsValidator _validator = new();

    [Fact]
    public void Default_options_pass()
    {
        _validator.Validate(null, new AgentPrismStructuredResponseOptions()).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void A_positive_MaxRepairAttempts_passes()
    {
        var options = new AgentPrismStructuredResponseOptions { MaxRepairAttempts = 2 };

        _validator.Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void A_negative_MaxRepairAttempts_fails()
    {
        var options = new AgentPrismStructuredResponseOptions { MaxRepairAttempts = -1 };

        var result = _validator.Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(failure => failure.Contains(nameof(AgentPrismStructuredResponseOptions.MaxRepairAttempts)));
    }
}
