using System.Globalization;
using CsCheck;
using Tracon.Tests.Differential;

namespace Tracon.Core.UnitTests.Tenancy;

/// <summary>
/// Properties of the tenant identifier fold that every store applies: folding twice is folding once, the letter-case
/// spellings of one identifier become one value, the result does not depend on the culture of the process, and a text
/// outside the allowed alphabet never gets as far as a store.
/// </summary>
public sealed class TenantNormalizationPropertyTests
{
    [Fact]
    public void Folding_a_folded_tenant_changes_nothing()
        => BoundaryValues.TenantId.Sample(
            static tenant =>
            {
                var once = AmbientTenantScope.Normalize(tenant);

                AmbientTenantScope.Normalize(once).ShouldBe(once, $"tenant {BoundaryValues.Show(tenant)}");
            },
            iter: 5_000);

    [Fact]
    public void The_letter_case_spellings_of_an_identifier_fold_to_one_value()
        => BoundaryValues.TenantId.Sample(
            static tenant =>
            {
                var expected = AmbientTenantScope.Normalize(tenant.ToLowerInvariant());

                AmbientTenantScope.Normalize(tenant).ShouldBe(expected);
                AmbientTenantScope.Normalize(tenant.ToUpperInvariant()).ShouldBe(expected);
                AmbientTenantScope.Normalize(Swap(tenant)).ShouldBe(expected);
            },
            iter: 5_000);

    [Fact]
    public void Folding_keeps_the_length_and_leaves_no_ascii_capital()
        => BoundaryValues.TenantId.Sample(
            static tenant =>
            {
                var folded = AmbientTenantScope.Normalize(tenant);

                folded.Length.ShouldBe(tenant.Length, $"tenant {BoundaryValues.Show(tenant)} changed length");
                folded.Any(static character => character is >= 'A' and <= 'Z').ShouldBeFalse($"tenant {BoundaryValues.Show(tenant)} kept a capital");
            },
            iter: 5_000);

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("az-Latn-AZ")]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void The_fold_does_not_depend_on_the_culture_of_the_process(string cultureName)
    {
        var original = CultureInfo.CurrentCulture;
        var originalUi = CultureInfo.CurrentUICulture;

        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            BoundaryValues.TenantId.Sample(
                static tenant => AmbientTenantScope.Normalize(tenant).ShouldBe(tenant.ToLowerInvariant(), $"tenant {BoundaryValues.Show(tenant)}"),
                iter: 2_000);

            // The one spelling a Turkish culture folds differently, written out.
            AmbientTenantScope.Normalize("TITLE").ShouldBe("title");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            CultureInfo.CurrentUICulture = originalUi;
        }
    }

    [Fact]
    public void A_text_with_a_character_outside_the_allowed_set_is_refused()
        => BoundaryValues.NonBlank(200)
            .Where(static tenant => tenant.Any(static character => !IsAllowed(character)))
            .Sample(
                static tenant =>
                {
                    Should.Throw<ArgumentException>(() => AmbientTenantScope.Normalize(tenant), $"tenant {BoundaryValues.Show(tenant)}");
                    AmbientTenantScope.TryNormalize(tenant, out _).ShouldBeFalse($"tenant {BoundaryValues.Show(tenant)}");
                },
                iter: 5_000);

    [Fact]
    public void A_text_of_the_allowed_set_folds_the_same_way_through_both_entry_points()
        => BoundaryValues.TenantId.Sample(
            static tenant =>
            {
                AmbientTenantScope.TryNormalize(tenant, out var canonical).ShouldBeTrue();
                canonical.ShouldBe(AmbientTenantScope.Normalize(tenant));
            },
            iter: 5_000);

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\n")]
    [InlineData(null)]
    public void A_blank_tenant_is_rejected(string? tenant)
        => Should.Throw<ArgumentException>(() => AmbientTenantScope.Normalize(tenant!));

    private static bool IsAllowed(char character)
        => character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '.' or '-';

    private static string Swap(string value)
        => string.Concat(value.Select(static character => char.IsUpper(character) ? char.ToLowerInvariant(character) : char.ToUpperInvariant(character)));
}
