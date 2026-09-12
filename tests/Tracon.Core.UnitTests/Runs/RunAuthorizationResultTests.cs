namespace Tracon.Core.UnitTests.Runs;

/// <summary>Tests for <see cref="RunAuthorizationResult"/>'s factory methods (Phase 139, F-185).</summary>
public sealed class RunAuthorizationResultTests
{
    [Fact]
    public void Allow_produces_an_allowed_result_with_no_reason()
    {
        var result = RunAuthorizationResult.Allow();

        result.IsAllowed.ShouldBeTrue();
        result.Reason.ShouldBeNull();
    }

    [Fact]
    public void Deny_produces_a_denied_result_carrying_the_reason()
    {
        var result = RunAuthorizationResult.Deny("not the owner");

        result.IsAllowed.ShouldBeFalse();
        result.Reason.ShouldBe("not the owner");
    }

    [Fact]
    public void Deny_with_null_reason_throws()
        => Should.Throw<ArgumentException>(static () => RunAuthorizationResult.Deny(null!));

    [Fact]
    public void Deny_with_whitespace_reason_throws()
        => Should.Throw<ArgumentException>(static () => RunAuthorizationResult.Deny("   "));
}
