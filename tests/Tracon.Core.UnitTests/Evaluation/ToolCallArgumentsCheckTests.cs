using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon.Core.UnitTests.Evaluation;

/// <summary>
/// F-605 (phase 227) - the <c>toolCallArgsMatch</c> check compares a JSON argument by
/// meaning, where MAF's own check compares the .NET type or the text.
/// </summary>
public sealed class ToolCallArgumentsCheckTests
{
    [Fact]
    public void A_number_equals_a_number_of_the_same_value_whatever_its_spelling_or_type()
    {
        foreach (var (actual, expected) in new (object Actual, bool Matches)[]
                 {
                     (Json("2"), true),
                     (Json("2.0"), true),
                     (Json("2.00"), true),
                     (Json("2e0"), true),
                     (2, true),
                     (2L, true),
                     (2.0, true),
                     (2m, true),
                     ((short)2, true),
                     (3, false),
                     (2.5, false),
                     ("2", false),
                     (true, false),
                 })
        {
            Matches(Json("2"), actual).ShouldBe(expected, $"expected 2, called with {actual}");
        }
    }

    [Fact]
    public void A_number_beyond_double_precision_is_not_rounded_into_its_neighbour()
    {
        Matches(Json("9007199254740993"), 9007199254740993L).ShouldBeTrue();
        Matches(Json("9007199254740993"), 9007199254740992L).ShouldBeFalse();
    }

    [Fact]
    public void An_object_equals_an_object_with_the_same_members_in_any_order_and_spacing()
    {
        const string Expected = """{"city":"Izmir","geo":{"lat":38.4,"lon":27.1},"tags":["a","b"]}""";

        foreach (var (actual, expected) in new (string Actual, bool Matches)[]
                 {
                     (Expected, true),
                     ("""{ "tags": ["a", "b"], "geo": {"lon": 27.10, "lat": 38.40}, "city": "Izmir" }""", true),
                     ("""{"city":"Izmir","geo":{"lat":38.4,"lon":27.1},"tags":["b","a"]}""", false),
                     ("""{"city":"Izmir","geo":{"lat":38.4},"tags":["a","b"]}""", false),
                     ("""{"city":"Izmir","geo":{"lat":38.4,"lon":27.1},"tags":["a","b"],"zip":"35000"}""", false),
                     ("""{"city":"Ankara","geo":{"lat":38.4,"lon":27.1},"tags":["a","b"]}""", false),
                     ("""["a","b"]""", false),
                     ("\"{}\"", false),
                 })
        {
            Matches(Json(Expected), Json(actual)).ShouldBe(expected, actual);
        }
    }

    [Fact]
    public void An_object_that_a_test_double_builds_from_dictionaries_and_sequences_compares_like_json()
    {
        var actual = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["tags"] = new[] { "a", "b" },
            ["geo"] = new Dictionary<string, object?>(StringComparer.Ordinal) { ["lon"] = 27.1, ["lat"] = 38.4 },
            ["city"] = "Izmir",
        };

        Matches(Json("""{"city":"Izmir","geo":{"lat":38.4,"lon":27.1},"tags":["a","b"]}"""), actual).ShouldBeTrue();
        Matches(Json("""{"city":"Izmir","geo":{"lat":38.4,"lon":27.1},"tags":["a"]}"""), actual).ShouldBeFalse();
    }

    [Fact]
    public void A_string_is_not_a_number_a_boolean_or_an_object()
    {
        Matches(Json("\"ORD-7\""), "ORD-7").ShouldBeTrue();
        Matches(Json("\"ORD-7\""), "ord-7").ShouldBeFalse();
        Matches(Json("\"true\""), true).ShouldBeFalse();
        Matches(Json("true"), true).ShouldBeTrue();
        Matches(Json("true"), false).ShouldBeFalse();
        Matches(Json("true"), "true").ShouldBeFalse();
        Matches(Json("false"), false).ShouldBeTrue();
        Matches(Json("\"\""), string.Empty).ShouldBeTrue();
        Matches(Json("""{"a":1}"""), """{"a":1}""").ShouldBeFalse();
    }

    [Fact]
    public void A_null_expectation_means_the_argument_is_absent_or_null()
    {
        var expected = new EvalExpectedToolCall { Name = "t", Arguments = Args(("note", "null")) };

        Run([("t", Dict(("note", null)))], expected).Passed.ShouldBeTrue();
        Run([("t", Dict(("note", Json("null"))))], expected).Passed.ShouldBeTrue();
        Run([("t", Dict(("other", 1)))], expected).Passed.ShouldBeTrue();
        Run([("t", Dict(("note", "x")))], expected).Passed.ShouldBeFalse();
        Run([("t", Dict(("note", string.Empty)))], expected).Passed.ShouldBeFalse();
    }

    [Fact]
    public void Extra_arguments_and_the_letter_case_of_names_do_not_matter()
    {
        var expected = new EvalExpectedToolCall { Name = "Get_Order_Status", Arguments = Args(("orderid", "\"ORD-1\"")) };

        Run([("get_order_status", Dict(("OrderId", "ORD-1"), ("verbose", true)))], expected).Passed.ShouldBeTrue();
        Run([("get_order_status", Dict(("verbose", true)))], expected).Passed.ShouldBeFalse();
    }

    [Fact]
    public void An_expectation_without_arguments_is_met_by_the_call_alone()
    {
        var result = Run([("get_order_status", null)], new EvalExpectedToolCall { Name = "get_order_status" });

        result.Passed.ShouldBeTrue();
        result.Reason.ShouldBe("Tool call args match: 1/1\n  get_order_status: called (args not checked)");
    }

    [Fact]
    public void An_empty_argument_set_checks_only_the_call_even_when_the_call_carried_no_arguments()
    {
        var expected = new EvalExpectedToolCall { Name = "ping", Arguments = Args() };

        Run([("ping", null)], expected).Passed.ShouldBeTrue();
        Run([("ping", Dict(("x", 1)))], expected).Passed.ShouldBeTrue();
    }

    [Fact]
    public void A_tool_that_was_not_called_fails_and_the_reason_names_it()
    {
        var result = Run([("other", null)], new EvalExpectedToolCall { Name = "get_order_status" });

        result.Passed.ShouldBeFalse();
        result.Reason.ShouldBe("Tool call args match: 0/1\n  get_order_status: not called");
        result.CheckName.ShouldBe("tool_call_args_match");
    }

    [Fact]
    public void A_mismatch_reads_args_mismatch_and_every_expectation_gets_a_line()
    {
        var result = Run(
            [("get_order_status", Dict(("orderId", "ORD-9999"))), ("get_shipping_status", null)],
            new EvalExpectedToolCall { Name = "get_order_status", Arguments = Args(("orderId", "\"ORD-1001\"")) },
            new EvalExpectedToolCall { Name = "get_shipping_status" },
            new EvalExpectedToolCall { Name = "refund" });

        result.Passed.ShouldBeFalse();
        result.Reason.ShouldBe(
            "Tool call args match: 1/3\n" +
            "  get_order_status: args mismatch\n" +
            "  get_shipping_status: called (args not checked)\n" +
            "  refund: not called");
    }

    [Fact]
    public void Any_call_to_the_tool_may_carry_the_arguments()
    {
        var expected = new EvalExpectedToolCall { Name = "lookup", Arguments = Args(("id", "2")) };

        Run([("lookup", Dict(("id", 1))), ("lookup", Dict(("id", 2)))], expected).Passed.ShouldBeTrue();
        Run([("lookup", Dict(("id", 1))), ("lookup", Dict(("id", 3)))], expected).Passed.ShouldBeFalse();
    }

    [Fact]
    public void A_call_without_an_argument_dictionary_cannot_meet_an_argument_expectation()
    {
        Run([("lookup", null)], new EvalExpectedToolCall { Name = "lookup", Arguments = Args(("id", "1")) })
            .Passed.ShouldBeFalse();
    }

    [Fact]
    public void An_item_that_expects_nothing_passes()
    {
        var result = Run([("lookup", null)]);

        result.Passed.ShouldBeTrue();
        result.Reason.ShouldBe("No expected tool calls specified.");
    }

    [Fact]
    public void The_reason_has_the_shape_of_the_MAF_check_for_the_cases_both_can_judge()
    {
        var expected = new[]
        {
            new EvalExpectedToolCall { Name = "get_order_status", Arguments = Args(("orderId", "\"ORD-1001\"")) },
            new EvalExpectedToolCall { Name = "refund", Arguments = Args(("reason", "\"late\"")) },
            new EvalExpectedToolCall { Name = "audit" },
            new EvalExpectedToolCall { Name = "missing" },
        };

        var item = ItemOf(
            [
                ("get_order_status", Dict(("orderId", "ORD-1001"))),
                ("refund", Dict(("reason", "damaged"))),
                ("audit", null),
            ],
            expected);

        var ours = ToolCallArgumentsCheck.Evaluate(item);
        var maf = EvalChecks.ToolCallArgsMatch().Invoke(item);

        ours.Passed.ShouldBe(maf.Passed);
        ours.Reason.ShouldBe(maf.Reason);
        ours.CheckName.ShouldBe(maf.CheckName);
    }

    [Fact]
    public void Attach_fills_the_form_MAF_reads_with_the_values_MAF_unwraps_to()
    {
        var item = ItemOf(
            [("create_ticket", Dict(("title", "Printer"), ("priority", 2), ("urgent", true)))],
            new EvalExpectedToolCall
            {
                Name = "create_ticket",
                Arguments = Args(("title", "\"Printer\""), ("priority", "2"), ("urgent", "true"), ("address", """{"city":"Izmir"}""")),
            },
            new EvalExpectedToolCall { Name = "close_ticket" });

        var maf = item.ExpectedToolCalls.ShouldNotBeNull();

        maf.Count.ShouldBe(2);
        maf[0].Name.ShouldBe("create_ticket");

        var arguments = maf[0].Arguments.ShouldNotBeNull();

        arguments["title"].ShouldBe("Printer");
        arguments["priority"].ShouldBe(2.0);
        arguments["urgent"].ShouldBe(true);
        arguments["address"].ShouldBe("""{"city":"Izmir"}""");
        maf[1].Arguments.ShouldBeNull();
    }

    [Fact]
    public void An_item_a_consumer_built_is_read_from_the_MAF_form()
    {
        var item = new EvalItem(
            "question",
            "answer",
            [new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("1", "lookup", Dict(("id", "ORD-7"), ("n", 2)))])])
        {
            ExpectedToolCalls = [new ExpectedToolCall("lookup", new Dictionary<string, object>(StringComparer.Ordinal) { ["id"] = "ORD-7", ["n"] = 2.0 })],
        };

        ToolCallArgumentsCheck.Evaluate(item).Passed.ShouldBeTrue();

        item.ExpectedToolCalls = [new ExpectedToolCall("lookup", new Dictionary<string, object>(StringComparer.Ordinal) { ["id"] = "ORD-8" })];

        ToolCallArgumentsCheck.Evaluate(item).Passed.ShouldBeFalse();
    }

    [Fact]
    public void An_item_a_factory_copied_still_meets_an_object_and_an_array_expectation()
    {
        // F-611: a copy does not carry the exact form, so the expected object arrives as its JSON
        // text while the call carries the object. MAF's check compares the text, and so does this one.
        var original = ItemOf(
            [("lookup", Dict(("address", Dict(("city", "Izmir"))), ("tags", new object[] { "a", "b" })))],
            new EvalExpectedToolCall { Name = "lookup", Arguments = Args(("address", """{"city":"Izmir"}"""), ("tags", """["a","b"]""")) });

        ToolCallArgumentsCheck.Evaluate(CopyOf(original)).Passed.ShouldBeTrue();
    }

    [Fact]
    public void An_item_a_factory_copied_judges_an_object_by_its_text_as_MAF_does()
    {
        var original = ItemOf(
            [("lookup", Dict(("address", Dict(("city", "Izmir"), ("zip", "35000")))))],
            new EvalExpectedToolCall { Name = "lookup", Arguments = Args(("address", """{"zip":"35000","city":"Izmir"}""")) });

        // The item the run built keeps the exact form, where key order plays no part.
        ToolCallArgumentsCheck.Evaluate(original).Passed.ShouldBeTrue();
        ToolCallArgumentsCheck.Evaluate(CopyOf(original)).Passed.ShouldBeFalse();
    }

    [Fact]
    public void An_item_a_factory_copied_keeps_a_text_expectation_apart_from_a_number()
    {
        var original = ItemOf(
            [("lookup", Dict(("id", 2)))],
            new EvalExpectedToolCall { Name = "lookup", Arguments = Args(("id", "\"2\"")) });

        ToolCallArgumentsCheck.Evaluate(CopyOf(original)).Passed.ShouldBeFalse();
    }

    [Fact]
    public void The_registry_builds_the_check_from_its_kind_name()
    {
        var registry = new EvalCheckRegistry([]);
        var checks = registry.BuildChecks(JsonDocument.Parse("""[{"kind":"toolCallArgsMatch"}]""").RootElement);

        var item = ItemOf(
            [("lookup", Dict(("id", 2)))],
            new EvalExpectedToolCall { Name = "lookup", Arguments = Args(("id", "2.0")) });

        checks.ShouldHaveSingleItem().Invoke(item).Passed.ShouldBeTrue();
    }

    [Fact]
    public void A_custom_check_cannot_take_the_name_of_the_new_built_in()
    {
        Should.Throw<TraconException>(() => new EvalCheckRegistry(
            [new TraconEvalCheckRegistration("toolCallArgsMatch", static _ => new EvalCheckResult(true, "x", "x"))]));
    }

    private static bool Matches(JsonElement expected, object? actual)
        => Run(
            [("t", Dict(("v", actual)))],
            new EvalExpectedToolCall { Name = "t", Arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["v"] = expected } }).Passed;

    private static EvalCheckResult Run(
        IEnumerable<(string Name, IDictionary<string, object?>? Arguments)> calls,
        params EvalExpectedToolCall[] expected)
        => ToolCallArgumentsCheck.Evaluate(ItemOf(calls, expected));

    private static EvalItem ItemOf(
        IEnumerable<(string Name, IDictionary<string, object?>? Arguments)> calls,
        params EvalExpectedToolCall[] expected)
    {
        var item = new EvalItem(
            "question",
            "answer",
            [new ChatMessage(ChatRole.Assistant, [.. calls.Select(static (call, index) => (AIContent)new FunctionCallContent($"call-{index}", call.Name, call.Arguments))])]);

        ToolCallArgumentsCheck.Attach(item, expected);

        return item;
    }

    // What an IEvalEvaluatorFactory that rebuilds each item hands the checks: the conversation and
    // MAF's form of the expectations, without the table entry Attach made.
    private static EvalItem CopyOf(EvalItem item)
        => new(item.Query, item.Response, [.. item.Conversation])
        {
            ExpectedToolCalls = item.ExpectedToolCalls,
        };

    private static Dictionary<string, object?> Dict(params (string Name, object? Value)[] values)
        => values.ToDictionary(static value => value.Name, static value => value.Value, StringComparer.Ordinal);

    private static Dictionary<string, JsonElement> Args(params (string Name, string Json)[] values)
        => values.ToDictionary(static value => value.Name, static value => Json(value.Json), StringComparer.Ordinal);

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }
}
