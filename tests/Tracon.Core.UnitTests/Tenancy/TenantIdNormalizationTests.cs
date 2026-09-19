using System.Globalization;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Tenancy;

/// <summary>
/// Verifies the canonical form of the tenant identifier (phase 179).
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
