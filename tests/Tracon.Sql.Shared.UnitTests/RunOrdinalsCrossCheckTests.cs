using System.Reflection;

namespace Tracon.SqlProviders.Tests;

/// <summary>
/// <c>RunOrdinals</c> and <c>SqlQueriesBase.RunColumnOrder</c> must describe the
/// exact same ordinal sequence: every ordinal from <c>0</c> to
/// <c>RunColumnOrder.Length - 1</c> named by exactly one <c>RunOrdinals</c> constant.
/// </summary>
/// <remarks>
/// A gap or a duplicate here means <c>SqlRunStore</c>'s reader and the documented
/// column sequence have drifted apart -- the exact "runs gets a new column, three
/// places must agree" trap the phase 94 ordinal contract exists to catch at build
/// time instead of at a wrong value silently reaching the API surface.
/// </remarks>
public sealed class RunOrdinalsCrossCheckTests
{
    [Fact]
    public void RunOrdinals_names_every_ordinal_in_RunColumnOrder_exactly_once()
    {
        var assembly = typeof(PostgresQueries).Assembly;

        var runColumnOrder = (Array)assembly.GetType("Tracon.SqlQueriesBase", throwOnError: true)!
            .GetField("RunColumnOrder", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        var runOrdinalsType = assembly.GetType("Tracon.RunOrdinals", throwOnError: true)!;
        var ordinalValues = runOrdinalsType
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(int))
            .Select(f => (int)f.GetRawConstantValue()!)
            .OrderBy(v => v)
            .ToArray();

        var expected = Enumerable.Range(0, runColumnOrder.Length).ToArray();

        ordinalValues.ShouldBe(
            expected,
            "RunOrdinals must name every ordinal from 0 to RunColumnOrder.Length - 1 exactly once " +
            "(no gap, no duplicate) -- a mismatch means SqlRunStore's reader and the documented " +
            "runs column sequence have drifted apart.");
    }
}
