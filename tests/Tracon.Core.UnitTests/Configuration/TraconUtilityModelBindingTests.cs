using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:UtilityModel</c> reaches <see cref="TraconOptions.UtilityModel"/>,
/// and a number that cannot be read stops the application at startup.
/// </summary>
/// <remarks>
/// 🚨 F-568: <c>Tracon:UtilityModel:Temperature = "abc"</c> failed the parse, the
/// out value stayed <c>0</c>, and the key was non-empty, so the binder wrote
/// <c>Temperature = 0</c>. The compaction summarizer ran at temperature zero and
/// nothing named the typo. <c>MaxOutputTokens = "1.5"</c> became <c>0</c> and
/// <c>TopP</c> the same. The F-385 rule (an unreadable value is not skipped) missed
/// this binder because it builds an immutable record in one expression.
/// </remarks>
public sealed class TraconUtilityModelBindingTests
{
    private const string Section = "Tracon:UtilityModel";

    [Fact]
    public void Every_key_of_the_section_is_bound()
    {
        var values = Required();
        values[$"{Section}:Temperature"] = "0.25";
        values[$"{Section}:MaxOutputTokens"] = "512";
        values[$"{Section}:TopP"] = "0.9";
        values[$"{Section}:ReasoningEffort"] = "Low";

        var binding = SectionBinding.Bind<TraconOptions>(values).UtilityModel.ShouldNotBeNull();

        string.Equals(binding.Provider, "openai", StringComparison.Ordinal).ShouldBeTrue();
        string.Equals(binding.Model, "gpt-5.4-mini", StringComparison.Ordinal).ShouldBeTrue();
        binding.Temperature.ShouldBe(0.25f);
        binding.MaxOutputTokens.ShouldBe(512);
        binding.TopP.ShouldBe(0.9f);
        string.Equals(binding.ReasoningEffort, "Low", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Temperature", "abc")]
    [InlineData("Temperature", "0,7")]
    [InlineData("TopP", "high")]
    [InlineData("MaxOutputTokens", "1.5")]
    [InlineData("MaxOutputTokens", "many")]
    public void An_unreadable_number_fails_at_startup(string key, string value)
    {
        var values = Required();
        values[$"{Section}:{key}"] = value;

        var failure = SectionBinding.FailsAtStartup(values);

        // The key and the text as written: bound as a number, the typo would read
        // "0" and the operator could not find it.
        failure.Message.ShouldContain($"Tracon:UtilityModel:{key} is '{value}'");
    }

    [Fact]
    public void Every_unreadable_number_is_named_in_one_failure()
    {
        var values = Required();
        values[$"{Section}:Temperature"] = "abc";
        values[$"{Section}:TopP"] = "high";

        var failure = SectionBinding.FailsAtStartup(values);

        failure.Message.ShouldContain("Tracon:UtilityModel:Temperature is 'abc'");
        failure.Message.ShouldContain("Tracon:UtilityModel:TopP is 'high'");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_number_means_the_provider_default(string value)
    {
        // An empty value is "not set", as before: only an unreadable one stops
        // the application.
        var values = Required();
        values[$"{Section}:Temperature"] = value;
        values[$"{Section}:MaxOutputTokens"] = value;
        values[$"{Section}:TopP"] = value;

        SectionBinding.StartsCleanly(values);

        var binding = SectionBinding.Bind<TraconOptions>(values).UtilityModel.ShouldNotBeNull();
        binding.Temperature.ShouldBeNull();
        binding.MaxOutputTokens.ShouldBeNull();
        binding.TopP.ShouldBeNull();
    }

    [Fact]
    public void A_zero_is_a_value_not_an_unreadable_one()
    {
        // Zero is a real temperature. The fix must not read "0" as "not set".
        var values = Required();
        values[$"{Section}:Temperature"] = "0";

        SectionBinding.StartsCleanly(values);

        SectionBinding.Bind<TraconOptions>(values).UtilityModel.ShouldNotBeNull().Temperature.ShouldBe(0f);
    }

    [Fact]
    public void Code_that_sets_the_utility_model_wins_over_an_unreadable_number()
    {
        // The failure comes from the bound binding, not from a record of the raw
        // text: code that assigns UtilityModel after binding replaces it, and the
        // application starts (K4, the F-385 precedent).
        var values = Required();
        values[$"{Section}:Temperature"] = "abc";

        SectionBinding.StartsCleanly(values, static (builder, _) => builder.Services.Configure<TraconOptions>(static options =>
            options.UtilityModel = new ModelBinding { Provider = "openai", Model = "gpt-5.4-nano" }));
    }

    [Fact]
    public void An_incomplete_binding_is_still_not_built()
    {
        // Provider and Model are both required (the documented rule); without
        // them nothing consumes the numbers, so a typo among them stops nothing.
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [$"{Section}:Provider"] = "openai",
            [$"{Section}:Temperature"] = "abc",
        };

        SectionBinding.StartsCleanly(values);

        SectionBinding.Bind<TraconOptions>(values).UtilityModel.ShouldBeNull();
    }

    [Fact]
    public void An_absent_section_leaves_no_utility_model()
    {
        SectionBinding.Bind<TraconOptions>(SectionBinding.NoKeys).UtilityModel.ShouldBeNull();
    }

    private static Dictionary<string, string?> Required() => new(StringComparer.Ordinal)
    {
        [$"{Section}:Provider"] = "openai",
        [$"{Section}:Model"] = "gpt-5.4-mini",
    };
}
