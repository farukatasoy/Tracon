namespace Tracon.Core.UnitTests.Security;

/// <summary>Tests for <see cref="TraconContentProtectionOptionsValidator"/>.</summary>
public sealed class TraconContentProtectionOptionsValidatorTests
{
    private readonly TraconContentProtectionOptionsValidator _validator = new();

    [Fact]
    public void Disabled_options_skip_the_active_key_check()
    {
        var options = new TraconContentProtectionOptions { Enabled = false };

        _validator.Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Disabled_options_with_an_empty_configuration_key_name_fail()
    {
        // Keys is read while protection is off too - an old row is still
        // decrypted through it - so the check no longer waits for Enabled.
        var options = new TraconContentProtectionOptions { Enabled = false };
        options.Keys["old"] = "";

        var result = _validator.Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(failure => failure.Contains("Keys['old']"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_undefined_column_fails_whatever_enabled_says(bool enabled)
    {
        var options = new TraconContentProtectionOptions { Enabled = enabled, ActiveKeyId = "k1" };
        options.Keys["k1"] = "Keys:k1";
        options.Columns.Add((ProtectedColumn)99);

        var result = _validator.Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(failure => failure.Contains("Columns contains 99"));
    }

    [Fact]
    public void Enabled_options_with_a_matching_active_key_pass()
    {
        var options = new TraconContentProtectionOptions { Enabled = true, ActiveKeyId = "k1" };
        options.Keys["k1"] = "Keys:k1";

        _validator.Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Enabled_options_without_an_active_key_id_fail()
    {
        var options = new TraconContentProtectionOptions { Enabled = true };

        var result = _validator.Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(failure => failure.Contains("ActiveKeyId"));
    }

    [Fact]
    public void Enabled_options_whose_active_key_id_has_no_entry_in_keys_fail()
    {
        var options = new TraconContentProtectionOptions { Enabled = true, ActiveKeyId = "k1" };

        var result = _validator.Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(failure => failure.Contains("k1"));
    }

    [Fact]
    public void Enabled_options_with_an_empty_configuration_key_name_fail()
    {
        var options = new TraconContentProtectionOptions { Enabled = true, ActiveKeyId = "k1" };
        options.Keys["k1"] = "   ";

        var result = _validator.Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(failure => failure.Contains("k1"));
    }
}
