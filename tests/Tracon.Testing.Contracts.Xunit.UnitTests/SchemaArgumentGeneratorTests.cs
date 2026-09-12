using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Tracon.Testing.Contracts.Tools;

namespace Tracon.Testing.Contracts.Xunit.UnitTests;

public sealed class SchemaArgumentGeneratorTests
{
    // A representative, real AIFunctionFactory-produced schema: one required
    // string, one bounded integer, one pattern-constrained optional string.
    // Not hand-authored JSON - 143.3 requires reading the shape the runtime
    // actually produces, not a shape this test assumes.
    private static AIFunction SearchTool { get; } = AIFunctionFactory.Create(
        (Func<string, int, string?, string>)Search,
        "search",
        "Searches things.");

    private static AIFunction NoArgumentsTool { get; } = AIFunctionFactory.Create((Func<string>)(() => "ok"), "status", "Returns a status.");

    private static AIFunction RequiredNestedObjectTool { get; } = AIFunctionFactory.Create(
        (Func<Address, string>)(static address => address.City),
        "ship",
        "Ships to an address.");

    [Fact]
    public void Same_seed_produces_the_same_missing_required_mutation()
    {
        var first = new SchemaArgumentGenerator(42).MissingRequiredProperty(SearchTool.JsonSchema);
        var second = new SchemaArgumentGenerator(42).MissingRequiredProperty(SearchTool.JsonSchema);

        first.IsSkipped.ShouldBeFalse();
        Canonical(first.Arguments!).ShouldBe(Canonical(second.Arguments!));
    }

    [Fact]
    public void Same_seed_produces_the_same_baseline()
    {
        var first = new SchemaArgumentGenerator(2026).Baseline(SearchTool.JsonSchema);
        var second = new SchemaArgumentGenerator(2026).Baseline(SearchTool.JsonSchema);

        first.IsSkipped.ShouldBeFalse();
        Canonical(first.Arguments!).ShouldBe(Canonical(second.Arguments!));
    }

    [Fact]
    public void Different_seeds_can_produce_different_baselines()
    {
        var first = new SchemaArgumentGenerator(1).Baseline(SearchTool.JsonSchema);
        var second = new SchemaArgumentGenerator(2).Baseline(SearchTool.JsonSchema);

        // Not a hard guarantee for every possible schema, but true for this
        // one (a free-form string plus a wide integer range) - if this ever
        // flakes for a schema change, the fix is a schema with more entropy,
        // not asserting less about the generator.
        string.Equals(Canonical(first.Arguments!), Canonical(second.Arguments!), StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void Missing_required_property_removes_a_required_key()
    {
        var mutation = new SchemaArgumentGenerator(1).MissingRequiredProperty(SearchTool.JsonSchema);

        mutation.IsSkipped.ShouldBeFalse();
        mutation.Arguments!.Keys.Contains("query", StringComparer.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void Type_mismatch_replaces_a_property_with_an_incompatible_json_kind()
    {
        var baseline = new SchemaArgumentGenerator(1).Baseline(SearchTool.JsonSchema);
        var mutation = new SchemaArgumentGenerator(1).TypeMismatch(SearchTool.JsonSchema);

        mutation.IsSkipped.ShouldBeFalse();
        mutation.Arguments!.Keys.Count.ShouldBe(baseline.Arguments!.Keys.Count);

        var changed = mutation.Arguments!.Any(pair
            => baseline.Arguments!.TryGetValue(pair.Key, out var original)
               && ((JsonElement)original!).ValueKind != ((JsonElement)pair.Value!).ValueKind);
        changed.ShouldBeTrue();
    }

    [Fact]
    public void Out_of_range_number_pushes_the_bounded_property_past_its_limit()
    {
        var mutation = new SchemaArgumentGenerator(1).OutOfRangeNumber(SearchTool.JsonSchema);

        mutation.IsSkipped.ShouldBeFalse();
        var limit = ((JsonElement)mutation.Arguments!["limit"]!).GetInt32();
        (limit is < 1 or > 100).ShouldBeTrue();
    }

    [Fact]
    public void Pattern_violation_replaces_the_pattern_constrained_property()
    {
        var mutation = new SchemaArgumentGenerator(1).PatternViolation(SearchTool.JsonSchema);

        mutation.IsSkipped.ShouldBeFalse();
        var tag = ((JsonElement)mutation.Arguments!["tag"]!).GetString();
        System.Text.RegularExpressions.Regex.IsMatch(
            tag ?? "", "^[a-z]+$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(200)).ShouldBeFalse();
    }

    [Fact]
    public void Extra_property_adds_a_key_the_schema_never_declared()
    {
        var baseline = new SchemaArgumentGenerator(1).Baseline(SearchTool.JsonSchema);
        var mutation = new SchemaArgumentGenerator(1).ExtraProperty(SearchTool.JsonSchema);

        mutation.IsSkipped.ShouldBeFalse();
        mutation.Arguments!.Keys.Count.ShouldBe(baseline.Arguments!.Keys.Count + 1);
    }

    [Fact]
    public void Empty_schema_skips_every_mutation_except_extra_property()
    {
        var generator = new SchemaArgumentGenerator(1);
        var schema = NoArgumentsTool.JsonSchema;

        generator.Baseline(schema).IsSkipped.ShouldBeFalse("an empty argument set is itself a valid baseline.");
        generator.MissingRequiredProperty(schema).IsSkipped.ShouldBeTrue();
        generator.TypeMismatch(schema).IsSkipped.ShouldBeTrue();
        generator.OutOfRangeNumber(schema).IsSkipped.ShouldBeTrue();
        generator.PatternViolation(schema).IsSkipped.ShouldBeTrue();
        generator.ExtraProperty(schema).IsSkipped.ShouldBeFalse("adding an unexpected key needs no declared property at all.");
    }

    [Fact]
    public void Every_skip_carries_a_non_empty_reason()
    {
        var generator = new SchemaArgumentGenerator(1);
        var schema = NoArgumentsTool.JsonSchema;

        generator.MissingRequiredProperty(schema).SkipReason.ShouldNotBeNullOrWhiteSpace();
        generator.TypeMismatch(schema).SkipReason.ShouldNotBeNullOrWhiteSpace();
        generator.OutOfRangeNumber(schema).SkipReason.ShouldNotBeNullOrWhiteSpace();
        generator.PatternViolation(schema).SkipReason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Required_nested_object_property_skips_the_baseline_with_a_reason()
    {
        // K-615: the generator does not model nested objects. A required
        // property shaped like one must make the baseline - and everything
        // built from it - skip explicitly, not produce a hollow argument set.
        var mutation = new SchemaArgumentGenerator(1).Baseline(RequiredNestedObjectTool.JsonSchema);

        mutation.IsSkipped.ShouldBeTrue();
        mutation.SkipReason.ShouldNotBeNullOrWhiteSpace();
        mutation.SkipReason!.ShouldContain("address");
    }

    [Fact]
    public void Poison_never_skips_for_a_readable_object_schema()
    {
        new SchemaArgumentGenerator(1).Poison(SearchTool.JsonSchema).IsSkipped.ShouldBeFalse();
        new SchemaArgumentGenerator(1).Poison(NoArgumentsTool.JsonSchema).IsSkipped.ShouldBeFalse();
    }

    [Fact]
    public void Unreadable_schema_throws_instead_of_silently_skipping()
    {
        using var document = JsonDocument.Parse("\"not-an-object\"");

        Should.Throw<InvalidOperationException>(() => new SchemaArgumentGenerator(1).Baseline(document.RootElement));
    }

    private static string Canonical(AIFunctionArguments arguments)
        => string.Join(
            ";",
            arguments
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => $"{pair.Key}={((JsonElement)pair.Value!).GetRawText()}"));

    private static string Search(
        [Description("query text")] string query,
        [Range(1, 100)] int limit = 10,
        [RegularExpression("^[a-z]+$")] string? tag = null)
        => query;

    private sealed record Address([property: Description("street")] string Street, string City);
}
