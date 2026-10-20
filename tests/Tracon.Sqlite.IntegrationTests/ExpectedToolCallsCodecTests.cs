using System.Text.Json;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// The text of <c>eval_cases.expected_tool_calls</c> (F-605, phase 227). The codec is shared by the
/// three SQL providers; the store contract proves the round trip against each database, and this
/// class proves the reading rules a database cannot be made to produce - damaged text, a row
/// older than the column.
/// </summary>
public sealed class ExpectedToolCallsCodecTests
{
    [Fact]
    public void No_calls_is_a_null_column()
    {
        ExpectedToolCallsCodec.Serialize(null).ShouldBeNull();
        ExpectedToolCallsCodec.Serialize([]).ShouldBeNull();
    }

    [Fact]
    public void A_call_without_arguments_leaves_the_key_out_and_an_empty_set_does_not()
    {
        var text = ExpectedToolCallsCodec.Serialize(
        [
            new EvalExpectedToolCall { Name = "a" },
            new EvalExpectedToolCall { Name = "b", Arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal) },
        ]);

        text.ShouldBe("""[{"name":"a"},{"name":"b","arguments":{}}]""");

        var calls = ExpectedToolCallsCodec.Deserialize(text, legacyNames: null);

        calls.Count.ShouldBe(2);
        calls[0].Arguments.ShouldBeNull();
        calls[1].Arguments.ShouldNotBeNull().ShouldBeEmpty();
    }

    [Fact]
    public void A_value_of_every_json_kind_comes_back_as_the_same_kind()
    {
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach (var (name, json) in new[]
                 {
                     ("text", "\"a \\\"quoted\\\" \\u00fc\""),
                     ("whole", "2"),
                     ("fraction", "0.25"),
                     ("big", "9007199254740993"),
                     ("yes", "true"),
                     ("no", "false"),
                     ("nothing", "null"),
                     ("list", "[1,[2],{\"k\":\"v\"}]"),
                     ("object", "{\"a\":{\"b\":[]}}"),
                 })
        {
            using var document = JsonDocument.Parse(json);
            arguments[name] = document.RootElement.Clone();
        }

        var text = ExpectedToolCallsCodec.Serialize([new EvalExpectedToolCall { Name = "t", Arguments = arguments }]);
        var read = ExpectedToolCallsCodec.Deserialize(text, legacyNames: null).ShouldHaveSingleItem().Arguments.ShouldNotBeNull();

        read.Count.ShouldBe(arguments.Count);

        foreach (var (name, value) in arguments)
        {
            read[name].ValueKind.ShouldBe(value.ValueKind, name);

            // The writer may spell an escape differently (\u0022 for \"); the text is what counts.
            (value.ValueKind == JsonValueKind.String ? read[name].GetString() : read[name].GetRawText())
                .ShouldBe(value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText(), name);
        }
    }

    [Fact]
    public void A_default_json_element_is_written_as_null_instead_of_failing_the_save()
    {
        var text = ExpectedToolCallsCodec.Serialize(
        [
            new EvalExpectedToolCall
            {
                Name = "t",
                Arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["unset"] = default },
            },
        ]);

        text.ShouldBe("""[{"name":"t","arguments":{"unset":null}}]""");
    }

    [Fact]
    public void A_row_older_than_the_column_reads_its_names_as_calls_without_arguments()
    {
        var calls = ExpectedToolCallsCodec.Deserialize(json: null, legacyNames: "get_order_status, get_shipping_status,, ");

        calls.Select(static call => call.Name).ShouldBe(["get_order_status", "get_shipping_status"]);
        calls.ShouldAllBe(static call => call.Arguments == null);
    }

    [Fact]
    public void The_new_column_wins_over_the_old_one_even_when_it_holds_an_empty_list()
    {
        ExpectedToolCallsCodec.Deserialize("""[{"name":"new"}]""", "old").Select(static call => call.Name).ShouldBe(["new"]);
        ExpectedToolCallsCodec.Deserialize("[]", "old").ShouldBeEmpty();
    }

    [Fact]
    public void A_row_with_nothing_in_either_column_expects_no_call()
    {
        ExpectedToolCallsCodec.Deserialize(null, null).ShouldBeEmpty();
        ExpectedToolCallsCodec.Deserialize(string.Empty, string.Empty).ShouldBeEmpty();
    }

    [Fact]
    public void Damaged_text_reads_as_an_expectation_no_run_can_meet_and_never_throws()
    {
        // The check passes a case that expects nothing, so damaged text read as an empty list
        // would quietly turn a case that asserts something into one that asserts nothing.
        foreach (var damaged in new[] { "{not json", "{\"name\":\"a\"}", "\"text\"", "42", "[{\"name\":" })
        {
            // The old column is not consulted: the new one is there and it is the one that is damaged.
            var calls = ExpectedToolCallsCodec.Deserialize(damaged, "legacy_tool");

            calls.Select(static call => call.Name).ShouldBe([ExpectedToolCallsCodec.UnreadableName], damaged);
        }

        ExpectedToolCallsCodec.Deserialize("{not json", null).ShouldHaveSingleItem()
            .Name.ShouldBe(ExpectedToolCallsCodec.UnreadableName);
    }

    [Fact]
    public void The_unreadable_name_is_not_a_name_a_tool_can_have()
    {
        // Tool names are letters, digits, underscores and dashes; the marker cannot be called.
        ExpectedToolCallsCodec.UnreadableName.ShouldContain("<", Case.Sensitive);
        ExpectedToolCallsCodec.UnreadableName.ShouldContain(" ", Case.Sensitive);
    }

    [Fact]
    public void An_entry_that_cannot_be_read_costs_the_case_an_unreadable_marker_but_keeps_the_readable_ones()
    {
        var calls = ExpectedToolCallsCodec.Deserialize(
            """[1, {"arguments":{"a":1}}, {"name":""}, {"name":7}, {"name":"keep","arguments":[1]}, {"name":"fine","arguments":{"a":1}}, {"name":"fine2","arguments":null}, {"name":"fine3"}]""",
            legacyNames: null);

        calls.Select(static call => call.Name).ShouldBe(["fine", "fine2", "fine3", ExpectedToolCallsCodec.UnreadableName]);
        calls[0].Arguments.ShouldNotBeNull().Keys.ShouldBe(["a"]);
        calls[1].Arguments.ShouldBeNull();
        calls[2].Arguments.ShouldBeNull();
    }

    [Fact]
    public void A_well_formed_list_carries_no_unreadable_marker()
    {
        ExpectedToolCallsCodec.Deserialize("""[{"name":"a"},{"name":"b","arguments":{}}]""", null)
            .Select(static call => call.Name).ShouldBe(["a", "b"]);
    }

    [Fact]
    public void A_value_read_back_outlives_the_document_it_was_parsed_from()
    {
        // JsonElement.Clone is the difference between a value and a dangling view of a disposed
        // buffer; an invalid element makes the whole list that carries the case fail to serialize.
        var call = ExpectedToolCallsCodec.Deserialize("""[{"name":"t","arguments":{"a":{"b":[1,2]}}}]""", null).ShouldHaveSingleItem();

        GC.Collect();

        JsonSerializer.Serialize(call.Arguments!["a"]).ShouldBe("""{"b":[1,2]}""");
    }
}
