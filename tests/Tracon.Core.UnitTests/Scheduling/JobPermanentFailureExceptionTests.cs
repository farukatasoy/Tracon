namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// <see cref="JobPermanentFailureException"/> takes a stable code and nothing else that reaches a stored text: the
/// code is checked when the exception is built, and the free text it may carry is kept apart from the message.
/// </summary>
public sealed class JobPermanentFailureExceptionTests
{
    [Theory]
    [InlineData("order.not-found")]
    [InlineData("a")]
    [InlineData("under_score")]
    [InlineData("0-first")]
    [InlineData("tracon-style.code_1")]
    public void A_code_of_lowercase_letters_digits_dot_underscore_and_hyphen_is_accepted(string code)
        => new JobPermanentFailureException(code).Code.ShouldBe(code);

    [Fact]
    public void A_code_of_the_longest_length_is_accepted()
        => new JobPermanentFailureException(new string('a', 64)).Code.Length.ShouldBe(64);

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("has space")]
    [InlineData("Order.NotFound")]
    [InlineData("order/not-found")]
    [InlineData("café.missing")]
    [InlineData("line\nbreak")]
    [InlineData("(ref: 00000000)")]
    public void A_code_outside_the_pattern_is_refused(string code)
        => Should.Throw<ArgumentException>(() => new JobPermanentFailureException(code))
            .ParamName.ShouldBe("code");

    [Fact]
    public void A_code_of_65_characters_is_refused()
        => Should.Throw<ArgumentException>(() => new JobPermanentFailureException(new string('a', 65)));

    [Fact]
    public void A_missing_code_is_refused()
        => Should.Throw<ArgumentNullException>(() => new JobPermanentFailureException(null!));

    [Fact]
    public void The_free_text_stays_out_of_the_message()
    {
        var cause = new TimeoutException("the database did not answer");
        var exception = new JobPermanentFailureException("order.not-found", "Order 42 of Jane Doe is not there.", cause);

        exception.Reason.ShouldBe("Order 42 of Jane Doe is not there.");
        exception.InnerException.ShouldBeSameAs(cause);
        exception.Message.ShouldContain("order.not-found");
        exception.Message.ShouldNotContain("Jane", Case.Insensitive);
        exception.Message.ShouldNotContain("database", Case.Insensitive);
    }

    [Fact]
    public void The_error_type_is_the_cataloged_value()
    {
        new JobPermanentFailureException("order.not-found").ErrorType.ShouldBe("job_permanent_failure");
        JobPermanentFailureException.JobPermanentFailureErrorType.ShouldBe(TraconErrorTypes.JobPermanentFailure);
    }
}
