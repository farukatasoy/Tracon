using System.Reflection;

namespace Tracon.SqlProviders.Tests;

/// <summary>
/// The SQL twin of K-483: <c>SqlQueriesBase.CostAddends</c> and
/// <see cref="RunCost.Total"/> must sum the exact same terms.
/// </summary>
/// <remarks>
/// K-483 was born when <c>InputCost + OutputCost</c> was hand-written seven
/// times in C#; a third term (the cache charge) reached <see cref="RunCost.Total"/>
/// but was missed in one spot, and a tenant with a cost ceiling could exceed it.
/// Phase 94 gave the SQL side the same single source (<c>CostAddends</c>), but a
/// list can still drift from its C# twin silently: nothing stops someone from
/// adding a fourth SQL addend without touching <see cref="RunCost"/>, or the
/// reverse. This test makes that drift a build failure by comparing the two
/// sets, not by asserting a hard-coded count on either side.
/// </remarks>
/// <remarks>
/// Phase 132 (F-175) added applied-unit-price fields to <see cref="RunCost"/>
/// (<c>*PricePerMillionTokens</c>) that are also <c>decimal?</c> but are rates,
/// not amounts, and are deliberately never summed by <see cref="RunCost.Total"/>.
/// The property filter below narrows to names ending in <c>Cost</c> so the
/// cross-check still catches a real addend drift without treating every
/// <c>decimal?</c> property as one.
/// </remarks>
public sealed class CostAddendsCrossCheckTests
{
    [Fact]
    public void CostAddends_match_the_decimal_properties_RunCost_Total_sums()
    {
        var costAddends = (string[])typeof(PostgresQueries).BaseType!
            .GetField("CostAddends", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        var runCostTerms = typeof(RunCost)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(decimal?) && p.Name.EndsWith("Cost", StringComparison.Ordinal))
            .Select(p => ToSnakeCase(p.Name))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        var sortedAddends = costAddends.OrderBy(n => n, StringComparer.Ordinal).ToArray();

        sortedAddends.ShouldBe(
            runCostTerms,
            "SqlQueriesBase.CostAddends and the decimal? properties RunCost.Total() sums must name " +
            "the exact same terms -- a term on one side with no counterpart on the other is exactly " +
            "how K-483 happened (a cost addend that reaches the SQL but not the C# sum, or vice versa).");
    }

    private static string ToSnakeCase(string pascalCase)
    {
        var result = new System.Text.StringBuilder();

        for (var i = 0; i < pascalCase.Length; i++)
        {
            var current = pascalCase[i];

            if (i > 0 && char.IsUpper(current))
            {
                result.Append('_');
            }

            result.Append(char.ToLowerInvariant(current));
        }

        return result.ToString();
    }
}
