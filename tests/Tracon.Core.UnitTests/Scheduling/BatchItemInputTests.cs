namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// How a batch item's stored text is read: plain text, or a structured item
/// that carries parameter values and documents.
/// </summary>
/// <remarks>
/// 🚨 F-294: an item is stored as text with no type, so a plain-text item can
/// look exactly like a structured one. These cases pin which texts count as
/// structured, so that nothing that was plain text before is silently
/// reinterpreted, and the one documented ambiguity stays the only one.
/// </remarks>
public sealed class BatchItemInputTests
{
    [Theory]
    [InlineData("summarise this")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[1,2,3]")]
    [InlineData("{not json")]
    [InlineData("\"quoted\"")]
    [InlineData("""{"orderId": 7}""")]
    [InlineData("""{"message": 7}""")]
    [InlineData("""{"message": null}""")]
    [InlineData("""{"Message": "capitalised member is not the shape"}""")]
    public void Anything_that_is_not_an_object_with_a_string_message_is_plain_text_exactly_as_before(string text)
    {
        var input = BatchItemInput.Parse(text);

        input.IsStructured.ShouldBeFalse();
        input.Message.ShouldBe(text);
        input.Problem.ShouldBeNull();
        input.Parameters.ShouldBeNull();
        input.Documents.ShouldBeEmpty();
    }

    [Fact]
    public void A_structured_item_yields_its_message_values_and_documents_in_order()
    {
        var input = BatchItemInput.Parse(
            """
            {
              "message": "go",
              "parameters": { "tone": "formal", "n": "3" },
              "documents": [ { "name": "a.txt", "content": "A" }, { "name": "b.txt", "content": "B" } ]
            }
            """);

        input.IsStructured.ShouldBeTrue();
        input.Problem.ShouldBeNull();
        input.Message.ShouldBe("go");
        input.Parameters.ShouldNotBeNull().Count.ShouldBe(2);
        input.Parameters["tone"].ShouldBe("formal");
        input.HasParameterValues.ShouldBeTrue();
        input.Documents.Select(static document => document.Name).ShouldBe(["a.txt", "b.txt"]);
        input.Documents[1].Content.ShouldBe("B");
    }

    [Fact]
    public void A_structured_item_with_only_a_message_has_no_values()
    {
        var input = BatchItemInput.Parse("""{"message":"go"}""");

        input.IsStructured.ShouldBeTrue();
        input.Message.ShouldBe("go");
        input.HasParameterValues.ShouldBeFalse();
        input.Documents.ShouldBeEmpty();
    }

    [Fact]
    public void Leading_whitespace_before_the_object_is_still_structured()
        => BatchItemInput.Parse("  \n {\"message\":\"go\"}").IsStructured.ShouldBeTrue();

    [Theory]
    [InlineData("""{"message":"go","parameters":"tone=formal"}""", "'parameters' must be an object")]
    [InlineData("""{"message":"go","parameters":{"n":3}}""", "parameter 'n' is not a string")]
    [InlineData("""{"message":"go","documents":{"name":"a"}}""", "'documents' must be an array")]
    [InlineData("""{"message":"go","documents":[{"name":"a"}]}""", "document 0 must be an object")]
    [InlineData("""{"message":"go","documents":[{"name":"a","content":"A"},"b"]}""", "document 1 must be an object")]
    [InlineData("""{"message":"go","parameter":{"tone":"formal"}}""", "unknown member 'parameter'")]
    public void A_malformed_structured_item_says_why_and_is_never_run_as_plain_text(string text, string reason)
    {
        var input = BatchItemInput.Parse(text);

        input.IsStructured.ShouldBeTrue();
        input.Problem.ShouldNotBeNull().ShouldContain(reason, Case.Sensitive);
    }

    [Fact]
    public void The_problem_text_never_carries_a_value()
    {
        // The text reaches a job item's error and a log line; a value is user content.
        var input = BatchItemInput.Parse(
            """{"message":"go","parameters":{"secretName":"hunter2-value"},"documents":[{"name":"n","content":"classified-body"},7]}""");

        input.Problem.ShouldNotBeNull();
        input.Problem.ShouldNotContain("hunter2-value", Case.Sensitive);
        input.Problem.ShouldNotContain("classified-body", Case.Sensitive);
    }

    [Fact]
    public void A_null_input_is_rejected()
        => Should.Throw<ArgumentNullException>(() => BatchItemInput.Parse(null!));

    [Theory]
    [InlineData("""{"message":"hi","parameters":{"tone":"injected"}}""")]
    [InlineData("""{"message":"hello","user":"bob"}""")]
    [InlineData("""{"message":"go","documents":[{"name":"a","content":"A"}]}""")]
    [InlineData("""plain "quoted" text with a \ and <tags> and naïve café""")]
    [InlineData("")]
    public void An_enveloped_text_always_comes_back_as_the_message_alone(string text)
    {
        // 🚨 F-294: a webhook body is the sender's text. Wrapped, it can name no
        // parameter value and cannot fail the item as malformed, whatever it holds.
        var input = BatchItemInput.Parse(BatchItemInput.Envelope(text));

        input.Message.ShouldBe(text);
        input.IsStructured.ShouldBeTrue();
        input.Problem.ShouldBeNull();
        input.Parameters.ShouldBeNull();
        input.Documents.ShouldBeEmpty();
    }

    [Fact]
    public void Wrapping_a_null_text_is_rejected()
        => Should.Throw<ArgumentNullException>(() => BatchItemInput.Envelope(null!));
}
