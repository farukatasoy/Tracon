using System.Reflection;
using System.Text.RegularExpressions;

namespace Tracon.SqlProviders.Tests;

/// <summary>
/// No SQLite query adds an amount of money with the engine's own arithmetic.
/// </summary>
/// <remarks>
/// <para>
/// SQLite turns a TEXT decimal into a binary double before it adds it, so <c>SUM(input_cost)</c> and
/// <c>cost + excluded.cost</c> are inexact (<c>0.1 + 0.2</c> is <c>0.30000000000000004</c>) although the columns hold exact
/// text. The exact sum is the connection function <c>tracon_decimal_sum</c> and the exact addition is
/// <c>tracon_decimal_add</c>. A new query written with a bare <c>SUM</c> or <c>+</c> over a money column would pass every
/// contract test that happens to use amounts a double holds, and fail a tenant whose cap sits on the last digit.
/// </para>
/// <para>
/// The scan reads the query text only, as <see cref="SqlTextSnapshotTests"/> does, and needs no database. A money column is
/// one whose name ends in <c>cost</c> or <c>price_per_mtok</c>.
/// </para>
/// </remarks>
public sealed class SqliteExactDecimalSumTests
{
    private const string SqliteTablePrefix = "tracon_";

    private const string MoneyColumn = @"(?:\w+\.)?(?:\w*_cost|cost|\w*_price_per_mtok)";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private static readonly Regex BareSum = new(
        $@"\b(?:SUM|AVG|TOTAL)\s*\(\s*{MoneyColumn}\s*\)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);

    private static readonly Regex BareArithmetic = new(
        $@"\b{MoneyColumn}\s*[-+*/]|[-+*/]\s*{MoneyColumn}\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);

    [Fact]
    public void No_query_sums_or_adds_a_money_column_with_the_engines_own_arithmetic()
    {
        var offenders = new List<string>();

        foreach (var (name, text) in QueryTexts())
        {
            foreach (Match match in BareSum.Matches(text))
            {
                offenders.Add($"{name}: {match.Value}");
            }

            foreach (Match match in BareArithmetic.Matches(text))
            {
                offenders.Add($"{name}: {match.Value}");
            }
        }

        offenders.ShouldBeEmpty(
            "write the sum as tracon_decimal_sum(...) and the addition as tracon_decimal_add(...): the engine adds a TEXT decimal as a double");
    }

    [Fact]
    public void The_exact_cost_total_carries_every_term_of_the_cost_addends()
    {
        var addends = (string[])typeof(SqliteQueries).BaseType!
            .GetField("CostAddends", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        var statistics = new SqliteQueries(SqliteTablePrefix).SelectRunStatistics;

        statistics.ShouldContain("tracon_decimal_sum(");

        foreach (var addend in addends)
        {
            statistics.ShouldContain(addend, Case.Sensitive, $"the cost total of the statistics must carry {addend}");
        }

        // The terms go into ONE aggregate: a "+" between two sums is the float addition this test exists to forbid.
        Regex.IsMatch(statistics, @"tracon_decimal_sum\(\s*input_cost\s*,\s*output_cost\s*,\s*cached_input_cost\s*,\s*cache_write_cost\s*\)", RegexOptions.CultureInvariant, RegexTimeout)
            .ShouldBeTrue("the four terms are summed together in one call");
    }

    private static IEnumerable<(string Name, string Text)> QueryTexts()
    {
        var queries = new SqliteQueries(SqliteTablePrefix);

        return queries.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.PropertyType == typeof(string) && property.GetIndexParameters().Length == 0)
            .Select(property => (property.Name, Text: (string?)property.GetValue(queries) ?? string.Empty))
            .Where(static query => query.Text.Length > 0);
    }
}
