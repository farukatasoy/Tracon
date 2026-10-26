namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:ContentGuard:Pattern</c> really reaches
/// <see cref="PatternContentGuardOptions"/>, and a PII list that cannot be read
/// stops the application at startup.
/// </summary>
/// <remarks>
/// 🚨 F-247: one typo in <c>MaskedPii</c> (<c>"Emial,CreditCard"</c>) failed the
/// whole parse, the value stayed <see cref="PiiPatterns.None"/> and PII masking was
/// off — including the correctly spelled <c>CreditCard</c>. A number with an
/// undefined bit (<c>"64"</c>) was accepted. The options had no validator.
/// </remarks>
public sealed class PatternContentGuardOptionsBindingTests
{
    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:ContentGuard:Pattern:MaskedPii"] = "Email,CreditCard",
            ["Tracon:ContentGuard:Pattern:MaskReplacement"] = "[hidden]",
        };

        var options = SectionBinding.Bind<PatternContentGuardOptions>(values);

        options.MaskedPii.ShouldBe(PiiPatterns.Email | PiiPatterns.CreditCard);
        string.Equals(options.MaskReplacement, "[hidden]", StringComparison.Ordinal).ShouldBeTrue();
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:ContentGuard:Pattern", options, "DeniedTerms");
    }

    [Fact]
    public void The_denied_terms_are_bound()
    {
        var options = SectionBinding.Bind<PatternContentGuardOptions>(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:ContentGuard:Pattern:DeniedTerms:0"] = "project-falcon",
            ["Tracon:ContentGuard:Pattern:DeniedTerms:1"] = "codename",
        });

        options.DeniedTerms.ShouldBe(["project-falcon", "codename"]);
    }

    [Fact]
    public void An_unreadable_pii_list_fails_at_startup()
    {
        var failure = SectionBinding.FailsAtStartup(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:ContentGuard:Pattern:MaskedPii"] = "Emial,CreditCard",
        });

        // The key and the text as written: one typo used to switch PII masking off without a word.
        failure.Message.ShouldContain("Tracon:ContentGuard:Pattern:MaskedPii is 'Emial,CreditCard'");
    }

    [Theory]
    [InlineData("64")]
    [InlineData("-1")]
    public void A_pii_number_with_an_undefined_bit_fails_at_startup(string value)
    {
        // A number parses, so the binder takes it; the options validator refuses the bit no member defines.
        var failure = SectionBinding.FailsAtStartup(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:ContentGuard:Pattern:MaskedPii"] = value,
        });

        failure.Message.ShouldContain($"PatternContentGuardOptions.MaskedPii is {value}");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_pii_list_means_none(string value)
    {
        // An empty value is "not set", as before: only an unreadable one stops
        // the application.
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:ContentGuard:Pattern:MaskedPii"] = value,
        };

        SectionBinding.StartsCleanly(values);
        SectionBinding.Bind<PatternContentGuardOptions>(values).MaskedPii.ShouldBe(PiiPatterns.None);
    }

    [Fact]
    public void Code_that_sets_the_pii_list_wins_over_an_unreadable_value()
    {
        // The failure comes from the bound value (K4, the F-385 precedent). The
        // sample application writes MaskedPii in code; a typo in its configuration
        // file must not keep it from starting.
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:ContentGuard:Pattern:MaskedPii"] = "Emial,CreditCard",
        };

        static void Compose(ITraconBuilder builder, Microsoft.Extensions.Configuration.IConfigurationSection _)
            => builder.AddPatternContentGuard(static options => options.MaskedPii = PiiPatterns.Email);

        SectionBinding.StartsCleanly(values, Compose);
        SectionBinding.Bind<PatternContentGuardOptions>(values, Compose).MaskedPii.ShouldBe(PiiPatterns.Email);
    }

    [Fact]
    public void An_undefined_pii_value_from_code_fails_at_startup_too()
    {
        var failure = SectionBinding.FailsAtStartup(SectionBinding.NoKeys, static (builder, _) =>
            builder.AddPatternContentGuard(static options => options.MaskedPii = (PiiPatterns)64));

        failure.Message.ShouldContain("PatternContentGuardOptions.MaskedPii is 64");
    }

    [Fact]
    public void An_absent_section_leaves_the_shipped_defaults()
    {
        var options = SectionBinding.Bind<PatternContentGuardOptions>(SectionBinding.NoKeys);

        options.DeniedTerms.ShouldBeEmpty();
        options.MaskedPii.ShouldBe(PiiPatterns.None);
        string.Equals(options.MaskReplacement, "[redacted]", StringComparison.Ordinal).ShouldBeTrue();
    }
}
