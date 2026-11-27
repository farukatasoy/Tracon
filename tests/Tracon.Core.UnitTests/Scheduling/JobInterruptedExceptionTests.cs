namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// <see cref="JobInterruptedException"/> carries a cataloged error type and a fixed message: the reason a handler gives is
/// for the log, and never part of a text that is stored when the exception ends a failed attempt.
/// </summary>
public sealed class JobInterruptedExceptionTests
{
    [Fact]
    public void The_error_type_is_the_cataloged_value()
    {
        new JobInterruptedException().ErrorType.ShouldBe("job_interrupted");
        JobInterruptedException.JobInterruptedErrorType.ShouldBe(TraconErrorTypes.JobInterrupted);
    }

    [Fact]
    public void Without_a_reason_there_is_none_and_the_message_is_the_fixed_text()
    {
        var exception = new JobInterruptedException();

        exception.Reason.ShouldBeNull();
        exception.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void The_message_is_the_same_with_and_without_a_reason()
        => new JobInterruptedException("The host stopped before the first call out.").Message
            .ShouldBe(new JobInterruptedException().Message);

    [Fact]
    public void The_reason_is_kept_for_the_log_and_stays_out_of_the_message()
    {
        var exception = new JobInterruptedException("Order 42 of Jane Doe was not charged yet.");

        exception.Reason.ShouldBe("Order 42 of Jane Doe was not charged yet.");
        exception.Message.ShouldNotContain("Jane", Case.Insensitive);
        exception.Message.ShouldNotContain("Order 42", Case.Insensitive);
    }

    [Fact]
    public void It_is_a_tracon_exception_with_no_inner_exception()
    {
        var exception = new JobInterruptedException("reason");

        exception.ShouldBeAssignableTo<TraconException>();
        exception.InnerException.ShouldBeNull();
    }
}
