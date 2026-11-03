using System.Globalization;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Tenancy;

/// <summary>
/// Verifies the canonical form of the tenant identifier.
/// </summary>
/// <remarks>
/// The rule itself is <see cref="AmbientTenantScope.Normalize"/>. What it
/// protects is measured at the HTTP boundary (<c>TenantIdCaseTests</c>) and at
/// the storage boundary (the store contracts); these cases pin the rule and
/// the two culture traps around it.
/// </remarks>
public sealed class TenantIdNormalizationTests
{
    [Theory]
    [InlineData("acme", "acme")]
    [InlineData("Acme", "acme")]
    [InlineData("ACME", "acme")]
    [InlineData("AcMe-1.Prod_2", "acme-1.prod_2")]
    public void Normalize_folds_to_invariant_lower_case(string input, string expected)
        => AmbientTenantScope.Normalize(input).ShouldBe(expected);

    [Fact]
    public void Normalize_is_idempotent()
    {
        var once = AmbientTenantScope.Normalize("Acme");

        AmbientTenantScope.Normalize(once).ShouldBe(once);
    }

    [Fact]
    public void A_canonical_identifier_comes_back_as_the_same_instance()
    {
        // The hot path of every store call: nothing is allocated for a value that is already canonical.
        var canonical = new string("acme-1.prod_2".AsSpan());

        ReferenceEquals(AmbientTenantScope.Normalize(canonical), canonical).ShouldBeTrue();
    }

    [Theory]
    [InlineData("m\u00FC\u015Fteri", 1, "U+00FC")]
    [InlineData("e\u0301cole", 1, "U+0301")]
    [InlineData("acme\uFF41", 4, "U+FF41")]
    [InlineData("ac me", 2, "U+0020")]
    [InlineData("acme*", 4, "U+002A")]
    [InlineData("acme/ops", 4, "U+002F")]
    [InlineData("acme:ops", 4, "U+003A")]
    [InlineData("acme\u0000", 4, "U+0000")]
    [InlineData("acme\u001Fops", 4, "U+001F")]
    [InlineData("acme\uD83D\uDE00", 4, "U+1F600")]
    public void Normalize_refuses_a_character_outside_the_allowed_set_and_names_where(string input, int index, string codePoint)
    {
        var failure = Should.Throw<ArgumentException>(() => AmbientTenantScope.Normalize(input));

        failure.ParamName.ShouldBe("tenantId");
        failure.Message.ShouldContain($"index {index}");
        failure.Message.ShouldContain(codePoint);
        failure.Message.ShouldNotContain(input.Trim('\u0000'), Case.Sensitive, "the message must not echo the client's text");
    }

    [Fact]
    public void Normalize_does_not_accept_the_retention_wildcard()
        => Should.Throw<ArgumentException>(() => AmbientTenantScope.Normalize("*"));

    [Theory]
    [InlineData("acme", "acme")]
    [InlineData("Acme-1.Prod_2", "acme-1.prod_2")]
    [InlineData("0", "0")]
    [InlineData(".", ".")]
    public void TryNormalize_returns_the_canonical_form_of_a_valid_identifier(string input, string expected)
    {
        AmbientTenantScope.TryNormalize(input, out var canonical).ShouldBeTrue();

        canonical.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("m\u00FC\u015Fteri")]
    [InlineData("e\u0301")]
    [InlineData("\uFF41")]
    [InlineData("*")]
    [InlineData("a b")]
    public void TryNormalize_answers_false_instead_of_throwing(string? input)
    {
        AmbientTenantScope.TryNormalize(input, out var canonical).ShouldBeFalse();

        canonical.ShouldBeNull();
    }

    [Fact]
    public void TryNormalize_and_Normalize_agree_on_every_character_of_the_basic_plane()
    {
        for (var code = 0; code <= 0xFFFF; code++)
        {
            var single = ((char)code).ToString();
            var accepted = AmbientTenantScope.TryNormalize(single, out var canonical);

            if (accepted)
            {
                AmbientTenantScope.Normalize(single).ShouldBe(canonical, $"U+{code:X4}");
            }
            else
            {
                Should.Throw<ArgumentException>(() => AmbientTenantScope.Normalize(single), $"U+{code:X4}");
            }
        }
    }

    [Fact]
    public void The_allowed_set_is_exactly_the_ascii_letters_digits_underscore_dot_and_hyphen()
    {
        var accepted = Enumerable.Range(0, 0x10000)
            .Select(static code => (char)code)
            .Where(static character => AmbientTenantScope.TryNormalize(character.ToString(), out _))
            .ToHashSet();

        var expected = new HashSet<char>("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_.-");

        accepted.SetEquals(expected).ShouldBeTrue("the alphabet of a tenant identifier is the one every collation keeps apart");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_rejects_a_blank_identifier(string? input)
        => Should.Throw<ArgumentException>(() => AmbientTenantScope.Normalize(input!));

    [Fact]
    public void NormalizeOrNull_passes_null_through()
    {
        // A null tenant means "every tenant" in the query types that take one.
        AmbientTenantScope.NormalizeOrNull(null).ShouldBeNull();
        AmbientTenantScope.NormalizeOrNull("Acme").ShouldBe("acme");
    }

    [Fact]
    public void Normalize_does_not_depend_on_the_current_culture()
    {
        // 🚨 Under tr-TR, ToLower() maps 'I' to the DOTLESS lower-case i. The
        // same tenant id would then fold to two different canonical values
        // depending on the server's culture, and a run started on one machine
        // could not read the rows another machine wrote. ToLowerInvariant is
        // the rule.
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

            AmbientTenantScope.Normalize("TENANT-ID").ShouldBe("tenant-id");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void The_ambient_scope_stores_the_canonical_form()
    {
        using (AmbientTenantScope.Begin("Acme"))
        {
            AmbientTenantScope.Current.ShouldBe("acme");
        }

        AmbientTenantScope.Current.ShouldBeNull();
    }

    [Fact]
    public void A_fixed_context_returns_the_canonical_form()
        => new FixedTenantContext("Acme").TenantId.ShouldBe("acme");

    [Fact]
    public void The_single_tenant_context_prefers_the_canonical_ambient_value()
    {
        var context = new SingleTenantContext(Options.Create(new TraconOptions()));

        context.TenantId.ShouldBe("default");

        using (AmbientTenantScope.Begin("ACME"))
        {
            context.TenantId.ShouldBe("acme");
        }
    }

    [Fact]
    public void A_non_canonical_default_tenant_is_rejected_at_startup()
    {
        // Rejected rather than folded: an operator who wrote "Acme" in
        // configuration and then reads "acme" in the audit trail has no way to
        // find out where the change happened.
        var result = new TraconOptionsValidator().Validate(
            name: null,
            new TraconOptions { DefaultTenantId = "Acme" });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("canonical");
        result.FailureMessage.ShouldContain("acme");
    }

    [Fact]
    public void The_default_value_of_the_default_tenant_is_already_canonical()
    {
        // No existing setup breaks by accident.
        var result = new TraconOptionsValidator().Validate(name: null, new TraconOptions());

        result.Failed.ShouldBeFalse();
    }
}
