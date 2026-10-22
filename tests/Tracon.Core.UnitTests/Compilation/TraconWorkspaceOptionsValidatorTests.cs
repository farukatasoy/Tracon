namespace Tracon.Core.UnitTests.Compilation;

/// <summary>Tests for <see cref="TraconWorkspaceOptionsValidator"/>.</summary>
public sealed class TraconWorkspaceOptionsValidatorTests
{
    private readonly TraconWorkspaceOptionsValidator _validator = new();

    [Fact]
    public void Default_options_pass()
        => _validator.Validate(null, new TraconWorkspaceOptions()).Succeeded.ShouldBeTrue();

    [Fact]
    public void Limits_that_are_equal_pass()
        => _validator.Validate(null, new TraconWorkspaceOptions { MaxFileBytes = 10, MaxSessionBytes = 10, MaxTenantBytes = 10 }).Succeeded.ShouldBeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_file_limit_below_one_fails(long value)
    {
        var result = _validator.Validate(null, new TraconWorkspaceOptions { MaxFileBytes = value });

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(static failure => failure.Contains(nameof(TraconWorkspaceOptions.MaxFileBytes), StringComparison.Ordinal));
    }

    [Fact]
    public void A_session_limit_below_the_file_limit_fails_because_a_session_could_not_hold_one_full_file()
    {
        var result = _validator.Validate(null, new TraconWorkspaceOptions { MaxFileBytes = 100, MaxSessionBytes = 99 });

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(static failure => failure.Contains(nameof(TraconWorkspaceOptions.MaxSessionBytes), StringComparison.Ordinal));
    }

    [Fact]
    public void A_tenant_limit_below_the_session_limit_fails_because_a_tenant_could_not_hold_one_full_session()
    {
        var result = _validator.Validate(null, new TraconWorkspaceOptions { MaxFileBytes = 10, MaxSessionBytes = 100, MaxTenantBytes = 99 });

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(static failure => failure.Contains(nameof(TraconWorkspaceOptions.MaxTenantBytes), StringComparison.Ordinal));
    }

    [Fact]
    public void Every_failure_is_reported_at_once()
    {
        var result = _validator.Validate(null, new TraconWorkspaceOptions { MaxFileBytes = 0, MaxSessionBytes = -5, MaxTenantBytes = -10 });

        result.Failures!.Count().ShouldBe(3);
    }
}
