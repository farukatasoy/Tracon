using System.Text.Json;

namespace AgentPrism.Core.UnitTests.Approvals;

/// <summary>
/// <see cref="ToolArgumentConditionMatcher"/>: the single comparison point for
/// argument-level approval conditions (Phase 63). Fails closed throughout.
/// </summary>
public sealed class ToolArgumentConditionMatcherTests
{
    [Fact]
    public void Empty_condition_list_always_matches()
        => ToolArgumentConditionMatcher.Matches([], Arguments()).ShouldBeTrue();

    [Fact]
    public void Number_at_or_under_threshold_matches()
        => Matches(Condition("amount", ToolArgumentOperator.LessThanOrEqual, 100), Arguments(("amount", 50)))
            .ShouldBeTrue();

    [Fact]
    public void Number_over_threshold_does_not_match()
        => Matches(Condition("amount", ToolArgumentOperator.LessThanOrEqual, 100), Arguments(("amount", 500)))
            .ShouldBeFalse();

    [Fact]
    public void Missing_path_does_not_match()
        => Matches(Condition("amount", ToolArgumentOperator.LessThanOrEqual, 100), Arguments())
            .ShouldBeFalse();

    [Fact]
    public void Type_mismatch_does_not_match_even_though_the_text_looks_equal()
        // "50" (text) must NOT satisfy a numeric rule — silent coercion would let
        // a caller dodge a numeric limit by sending its value as text.
        => Matches(Condition("amount", ToolArgumentOperator.LessThanOrEqual, 100), Arguments(("amount", "50")))
            .ShouldBeFalse();

    [Fact]
    public void Equals_compares_same_json_kind_only()
    {
        Matches(Condition("tier", ToolArgumentOperator.Equals, "gold"), Arguments(("tier", "gold"))).ShouldBeTrue();
        Matches(Condition("tier", ToolArgumentOperator.Equals, "gold"), Arguments(("tier", "silver"))).ShouldBeFalse();
        Matches(Condition("tier", ToolArgumentOperator.Equals, "gold"), Arguments(("tier", 1))).ShouldBeFalse();
    }

    [Fact]
    public void NotEquals_is_the_negation()
    {
        Matches(Condition("tier", ToolArgumentOperator.NotEquals, "gold"), Arguments(("tier", "silver"))).ShouldBeTrue();
        Matches(Condition("tier", ToolArgumentOperator.NotEquals, "gold"), Arguments(("tier", "gold"))).ShouldBeFalse();
    }

    [Fact]
    public void GreaterThan_only_accepts_numbers()
        => Matches(Condition("tier", ToolArgumentOperator.GreaterThan, "gold"), Arguments(("tier", "silver")))
            .ShouldBeFalse();

    [Fact]
    public void In_matches_membership()
    {
        var values = new[] { "eu", "us" };

        Matches(Condition("region", ToolArgumentOperator.In, values), Arguments(("region", "us"))).ShouldBeTrue();
        Matches(Condition("region", ToolArgumentOperator.In, values), Arguments(("region", "apac"))).ShouldBeFalse();
    }

    [Fact]
    public void NotIn_matches_non_membership()
    {
        var values = new[] { "eu", "us" };

        Matches(Condition("region", ToolArgumentOperator.NotIn, values), Arguments(("region", "apac"))).ShouldBeTrue();
        Matches(Condition("region", ToolArgumentOperator.NotIn, values), Arguments(("region", "us"))).ShouldBeFalse();
    }

    [Fact]
    public void Empty_in_list_never_matches()
        => Matches(Condition("region", ToolArgumentOperator.In, Array.Empty<string>()), Arguments(("region", "us")))
            .ShouldBeFalse();

    [Fact]
    public void Overlong_path_does_not_match()
    {
        var longPath = string.Join('.', Enumerable.Range(0, ToolArgumentConditionLimits.MaxPathSegments + 1).Select(i => $"p{i}"));

        Matches(Condition(longPath, ToolArgumentOperator.Equals, "x"), Arguments()).ShouldBeFalse();
    }

    [Fact]
    public void Nested_path_walks_into_a_json_object()
    {
        var order = JsonSerializer.Deserialize<JsonElement>("""{"amount":42}""");
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal) { ["order"] = order };

        Matches(Condition("order.amount", ToolArgumentOperator.Equals, 42), arguments).ShouldBeTrue();
    }

    [Fact]
    public void All_conditions_must_match()
    {
        var conditions = new[]
        {
            Condition("amount", ToolArgumentOperator.LessThanOrEqual, 100),
            Condition("tier", ToolArgumentOperator.Equals, "gold"),
        };

        ToolArgumentConditionMatcher.Matches(conditions, Arguments(("amount", 50), ("tier", "gold"))).ShouldBeTrue();
        ToolArgumentConditionMatcher.Matches(conditions, Arguments(("amount", 50), ("tier", "silver"))).ShouldBeFalse();
    }

    private static bool Matches(ToolArgumentCondition condition, IReadOnlyDictionary<string, object?> arguments)
        => ToolArgumentConditionMatcher.Matches([condition], arguments);

    private static ToolArgumentCondition Condition(string path, ToolArgumentOperator op, object value)
        => new()
        {
            Path = path,
            Operator = op,
            Value = JsonSerializer.SerializeToElement(value),
        };

    private static Dictionary<string, object?> Arguments(params (string Key, object? Value)[] pairs)
        => pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}
