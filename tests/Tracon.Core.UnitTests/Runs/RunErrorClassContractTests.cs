namespace Tracon.Core.UnitTests.Runs;

/// <summary>
/// Pins the wire contract of <see cref="RunErrorClass"/>.
/// </summary>
/// <remarks>
/// <para>
/// The numeric values reach consumers through three generated surfaces (the
/// OpenAPI document, the TypeScript schema and the generated C# client) and
/// through <c>run.error_class</c> in every SQL store, so a member may never be
/// renumbered. This test fails on ANY change to the set, which forces the
/// change to be a deliberate contract decision rather than a side effect.
/// </para>
/// <para>
/// 🚨 Value <c>9</c> is a RETIRED GAP, not a free slot. It held
/// <c>BudgetExceeded</c> ("the tree or context budget was exceeded"), which no
/// code path could ever produce: when the tree budget runs out
/// <c>ChildAgentInvoker</c> deliberately returns text to the model instead of
/// throwing, so the run ends successfully. The member was declared across the
/// whole shipped surface — OpenAPI, the TypeScript schema and both UI
/// dictionaries — and advertised a state that never occurred, so it was removed
/// while <c>PublicAPI.Shipped.txt</c> was still empty (K-603). Never reuse 9:
/// old records and old clients still carry the old meaning.
/// </para>
/// </remarks>
public sealed class RunErrorClassContractTests
{
    private static readonly (string Name, int Value)[] Expected =
    [
        ("Unknown", 0),
        ("ProviderError", 1),
        ("ProviderUnavailable", 2),
        ("RateLimited", 3),
        ("QuotaExceeded", 4),
        ("ContentFiltered", 5),
        ("ToolError", 6),
        ("Timeout", 7),
        ("CompilationFailed", 8),
        ("Canceled", 10),
        ("ContentBlocked", 11),
        ("Infrastructure", 12),
        ("ToolTimeout", 13),
        ("StructuredResponseInvalid", 14),
    ];

    [Fact]
    public void The_member_set_and_its_numeric_values_are_pinned()
    {
        var actual = Enum.GetValues<RunErrorClass>()
            .Select(value => (Name: value.ToString(), Value: (int)value))
            .OrderBy(member => member.Value)
            .ToArray();

        actual.ShouldBe(Expected);
    }

    [Fact]
    public void Value_nine_stays_retired()
    {
        Enum.IsDefined(typeof(RunErrorClass), 9).ShouldBeFalse(
            "value 9 held BudgetExceeded, which no code path could produce. Reusing it " +
            "would give stored records and old clients a second, conflicting meaning.");
    }
}
