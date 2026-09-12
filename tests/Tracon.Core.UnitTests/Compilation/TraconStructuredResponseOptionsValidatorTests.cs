namespace Tracon.Core.UnitTests.Compilation;

/// <summary>Tests for <see cref="TraconStructuredResponseOptionsValidator"/>.</summary>
public sealed class TraconStructuredResponseOptionsValidatorTests
{
    private readonly TraconStructuredResponseOptionsValidator _validator = new();

    [Fact]
    public void Default_options_pass()
    {
        _validator.Validate(null, new TraconStructuredResponseOptions()).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void A_positive_MaxRepairAttempts_passes()
    {
        var options = new TraconStructuredResponseOptions { MaxRepairAttempts = 2 };

        _validator.Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void A_negative_MaxRepairAttempts_fails()
    {
        var options = new TraconStructuredResponseOptions { MaxRepairAttempts = -1 };

        var result = _validator.Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(failure => failure.Contains(nameof(TraconStructuredResponseOptions.MaxRepairAttempts)));
    }
}
