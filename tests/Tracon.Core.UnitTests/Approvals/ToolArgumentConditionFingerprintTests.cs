using System.Text.Json;

namespace Tracon.Core.UnitTests.Approvals;

/// <summary>
/// <see cref="ToolArgumentConditionFingerprint"/>: the uniqueness key every
/// approval rule store compares, and the value the SQL stores persist as
/// <c>conditions_hash</c> (Phase 182, K-851).
/// </summary>
/// <remarks>
/// The store contract proves the two store-level outcomes (no merge, no
/// whitespace duplicate). These tests pin what the contract cannot: that a
/// set without a separator-bearing path still hashes to the value rows were
/// written with before the fingerprint moved here. A drift there turns every
/// stored rule into a duplicate the next time the same rule is saved.
/// </remarks>
public sealed class ToolArgumentConditionFingerprintTests
{
    /// <summary>
    /// SHA-256 of <c>amount␟5␟100␞region␟6␟["eu","us"]␞</c> - the pre-image the
    /// SQL stores built before the fingerprint moved into Core, computed
    /// independently of this code.
    /// </summary>
    private const string LegacyHash = "9BBD747F19F665FBA739326536582F871FACC0E12B8AE6F659A83271E5F98996";

    [Fact]
    public void A_set_without_separators_keeps_the_stored_legacy_hash()
        => ToolArgumentConditionFingerprint.Compute(
            [
                Condition("region", ToolArgumentOperator.In, """["eu", "us"]"""),
                Condition("amount", ToolArgumentOperator.LessThanOrEqual, "100"),
            ])
            .ShouldBe(LegacyHash);

    [Fact]
    public void The_empty_set_has_no_fingerprint()
        => ToolArgumentConditionFingerprint.Compute([]).ShouldBeNull();

    [Fact]
    public void Order_does_not_change_the_fingerprint()
    {
        var first = Condition("amount", ToolArgumentOperator.LessThanOrEqual, "100");
        var second = Condition("currency", ToolArgumentOperator.Equals, "\"EUR\"");

        ToolArgumentConditionFingerprint.Compute([first, second])
            .ShouldBe(ToolArgumentConditionFingerprint.Compute([second, first]));
    }

    [Fact]
    public void A_separator_bearing_path_cannot_impersonate_a_different_set()
    {
        var single = ToolArgumentConditionFingerprint.Compute(
            [Condition("amount\u001F0\u001F1\u001Ecurrency", ToolArgumentOperator.Equals, "2")]);
        var pair = ToolArgumentConditionFingerprint.Compute(
            [
                Condition("amount", ToolArgumentOperator.Equals, "1"),
                Condition("currency", ToolArgumentOperator.Equals, "2"),
            ]);

        string.Equals(single, pair, StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void Whitespace_inside_a_list_value_does_not_change_the_fingerprint()
        => ToolArgumentConditionFingerprint.Compute([Condition("region", ToolArgumentOperator.In, """[ "eu" , "us" ]""")])
            .ShouldBe(ToolArgumentConditionFingerprint.Compute([Condition("region", ToolArgumentOperator.In, """["eu","us"]""")]));

    private static ToolArgumentCondition Condition(string path, ToolArgumentOperator op, string json)
    {
        using var document = JsonDocument.Parse(json);

        return new ToolArgumentCondition { Path = path, Operator = op, Value = document.RootElement.Clone() };
    }
}
