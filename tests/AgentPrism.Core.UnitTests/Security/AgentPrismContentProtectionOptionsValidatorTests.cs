namespace AgentPrism.Core.UnitTests.Security;

/// <summary>Tests for <see cref="AgentPrismContentProtectionOptionsValidator"/>.</summary>
public sealed class AgentPrismContentProtectionOptionsValidatorTests
{
    private readonly AgentPrismContentProtectionOptionsValidator _validator = new();

    [Fact]
    public void Disabled_options_always_pass_regardless_of_shape()
    {
        var options = new AgentPrismContentProtectionOptions { Enabled = false };

        _validator.Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Enabled_options_with_a_matching_active_key_pass()
    {
        var options = new AgentPrismContentProtectionOptions { Enabled = true, ActiveKeyId = "k1" };
        options.Keys["k1"] = "Keys:k1";

        _validator.Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Enabled_options_without_an_active_key_id_fail()
    {
        var options = new AgentPrismContentProtectionOptions { Enabled = true };

        var result = _validator.Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(failure => failure.Contains("ActiveKeyId"));
    }

    [Fact]
    public void Enabled_options_whose_active_key_id_has_no_entry_in_keys_fail()
    {
        var options = new AgentPrismContentProtectionOptions { Enabled = true, ActiveKeyId = "k1" };

        var result = _validator.Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(failure => failure.Contains("k1"));
    }

    [Fact]
    public void Enabled_options_with_an_empty_configuration_key_name_fail()
    {
        var options = new AgentPrismContentProtectionOptions { Enabled = true, ActiveKeyId = "k1" };
        options.Keys["k1"] = "   ";

        var result = _validator.Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(failure => failure.Contains("k1"));
    }
}
