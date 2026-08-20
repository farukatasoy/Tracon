using System.Text.Json;
using Microsoft.Agents.AI;

namespace AgentPrism.Core.UnitTests.Evaluation;

public sealed class EvalCheckRegistryTests
{
    [Fact]
    public void Empty_or_undefined_payload_returns_an_empty_list()
    {
        var registry = new EvalCheckRegistry([]);

        registry.BuildChecks(default).ShouldBeEmpty();
        registry.BuildChecks(JsonDocument.Parse("null").RootElement).ShouldBeEmpty();
    }

    [Fact]
    public void Payload_that_is_not_an_array_throws()
    {
        var registry = new EvalCheckRegistry([]);
        var spec = Parse("""{"kind":"nonEmpty"}""");

        Should.Throw<AgentPrismException>(() => registry.BuildChecks(spec));
    }

    [Fact]
    public void Definition_without_a_kind_field_throws()
    {
        var registry = new EvalCheckRegistry([]);
        var spec = Parse("""[{"minLength":10}]""");

        Should.Throw<AgentPrismException>(() => registry.BuildChecks(spec));
    }

    [Fact]
    public void Unknown_check_kind_throws()
    {
        var registry = new EvalCheckRegistry([]);
        var spec = Parse("""[{"kind":"noSuchThing"}]""");

        Should.Throw<AgentPrismException>(() => registry.BuildChecks(spec));
    }

    [Fact]
    public void NonEmpty_works_with_the_correct_threshold()
    {
        var registry = new EvalCheckRegistry([]);
        var checks = registry.BuildChecks(Parse("""[{"kind":"nonEmpty","minLength":5}]"""));

        checks.Count.ShouldBe(1);
        checks[0].Invoke(new EvalItem("question", "no")).Passed.ShouldBeFalse();
        checks[0].Invoke(new EvalItem("question", "a long enough answer")).Passed.ShouldBeTrue();
    }

    [Fact]
    public void ContainsExpected_works_based_on_the_expected_output()
    {
        var registry = new EvalCheckRegistry([]);
        var checks = registry.BuildChecks(Parse("""[{"kind":"containsExpected","caseSensitive":false}]"""));

        var item = new EvalItem("question", "Answer about the RETURN process") { ExpectedOutput = "return" };

        checks[0].Invoke(item).Passed.ShouldBeTrue();
    }

    [Fact]
    public void ContainsExpected_always_fails_with_an_empty_ExpectedOutput()
    {
        // Phase 45 Open Question 2: a promoted case (Failed/NegativeScore)
        // carries an empty ExpectedOutput. Measured: EvalChecks.ContainsExpected
        // does NOT throw on a null/empty ExpectedOutput, it silently returns
        // PASSED=false — so such a case ALWAYS appears failed in a suite
        // containing `containsExpected` (docs/arsiv/fazlar/45-URETIMDEN-EVAL-KUMESI.md,
        // option B).
        var registry = new EvalCheckRegistry([]);
        var checks = registry.BuildChecks(Parse("""[{"kind":"containsExpected"}]"""));

        var item = new EvalItem("question", "any output") { ExpectedOutput = null };

        checks[0].Invoke(item).Passed.ShouldBeFalse();
    }

    [Fact]
    public void Keywords_searches_for_the_given_words()
    {
        var registry = new EvalCheckRegistry([]);
        var checks = registry.BuildChecks(Parse("""[{"kind":"keywords","values":["return","shipping"]}]"""));

        checks[0].Invoke(new EvalItem("question", "return and shipping process")).Passed.ShouldBeTrue();
        checks[0].Invoke(new EvalItem("question", "irrelevant answer")).Passed.ShouldBeFalse();
    }

    [Fact]
    public void ToolCalled_with_an_invalid_mode_throws()
    {
        var registry = new EvalCheckRegistry([]);
        var spec = Parse("""[{"kind":"toolCalled","tools":["get_order_status"],"mode":"everything"}]""");

        Should.Throw<AgentPrismException>(() => registry.BuildChecks(spec));
    }

    [Fact]
    public void Custom_check_registration_can_be_used()
    {
        EvalCheck custom = item =>
            new EvalCheckResult(string.Equals(item.Response, "expected", StringComparison.Ordinal), "custom", "customCheck");
        var registry = new EvalCheckRegistry([new AgentPrismEvalCheckRegistration("customCheck", custom)]);

        var checks = registry.BuildChecks(Parse("""[{"kind":"customCheck"}]"""));

        checks.Count.ShouldBe(1);
        checks[0].Invoke(new EvalItem("question", "expected")).Passed.ShouldBeTrue();
    }

    [Fact]
    public void Registering_the_same_custom_check_twice_throws()
    {
        EvalCheck custom = _ => new EvalCheckResult(true, "custom", "customCheck");

        Should.Throw<AgentPrismException>(() => new EvalCheckRegistry(
        [
            new AgentPrismEvalCheckRegistration("customCheck", custom),
            new AgentPrismEvalCheckRegistration("customCheck", custom),
        ]));
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
