using System.Reflection;
using System.Text.RegularExpressions;

namespace AgentPrism.SqlProviders.Tests;

/// <summary>
/// The <c>runs_v1</c> read contract view's <c>total_cost</c> column (Phase 111,
/// 111.5 "cost addend gate"): K-483's class -- a cost term added to
/// <c>SqlQueriesBase.CostAddends</c> without reaching every hand-written SQL
/// total -- must not repeat here.
/// </summary>
/// <remarks>
/// Reflects <c>SqlQueriesBase.CostAddends</c> (the single source
/// <see cref="CostAddendsCrossCheckTests"/> already cross-checks against
/// <c>RunCost.Total()</c>) and asserts every addend name appears inside the
/// <c>-- total_cost: BEGIN/END</c> block of each provider's embedded
/// <c>0001_read_views.sql</c>. A fourth <c>*_cost</c> column added to
/// <c>CostAddends</c> and forgotten here fails this test at build time,
/// before it ever under-reports a run's cost in production.
/// </remarks>
public sealed class ReadViewCostTermTests
{
    [Fact]
    public void PostgreSql_total_cost_sums_every_cost_addend()
        => AssertCostAddends(typeof(PostgresQueries).Assembly, "AgentPrism.PostgreSql.MigrationsViews.");

    [Fact]
    public void SqlServer_total_cost_sums_every_cost_addend()
        => AssertCostAddends(typeof(SqlServerQueries).Assembly, "AgentPrism.SqlServer.MigrationsViews.");

    [Fact]
    public void Sqlite_total_cost_sums_every_cost_addend()
        => AssertCostAddends(typeof(SqliteQueries).Assembly, "AgentPrism.Sqlite.MigrationsViews.");

    private static void AssertCostAddends(Assembly assembly, string resourcePrefix)
    {
        var costAddends = (string[])typeof(PostgresQueries).BaseType!
            .GetField("CostAddends", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        var totalCostBlock = ReadViewSqlText.ExtractMarkedBlock(assembly, resourcePrefix, "total_cost: BEGIN", "total_cost: END");

        foreach (var addend in costAddends)
        {
            Regex.IsMatch(totalCostBlock, $@"\b{Regex.Escape(addend)}\b", RegexOptions.None, TimeSpan.FromSeconds(2)).ShouldBeTrue(
                $"runs_v1.total_cost must sum '{addend}' -- SqlQueriesBase.CostAddends is the single " +
                "source of a run's cost terms (K-483) and a term missing here silently under-reports " +
                "cost in every consumer that reads runs_v1 directly.");
        }
    }
}
