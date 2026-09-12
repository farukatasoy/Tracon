using Tracon.Testing;

namespace Tracon.Testing.UnitTests;

public sealed class RunAssertionsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void ShouldHaveCompleted_passes_for_a_completed_run()
    {
        var assertions = Create(RunStatus.Completed);

        Should.NotThrow(() => assertions.ShouldHaveCompleted());
    }

    [Fact]
    public void ShouldHaveCompleted_fails_with_the_expected_and_actual_value_for_an_incomplete_run()
    {
        var assertions = Create(RunStatus.Failed);

        var exception = Should.Throw<TraconAssertionException>(() => assertions.ShouldHaveCompleted());

        exception.Message.ShouldContain("Completed", Case.Sensitive);
        exception.Message.ShouldContain("Failed", Case.Sensitive);
    }

    [Fact]
    public void ShouldHaveFailed_passes_for_a_failed_run()
    {
        var assertions = Create(RunStatus.Failed);

        Should.NotThrow(() => assertions.ShouldHaveFailed());
    }

    [Fact]
    public void ShouldHaveFailed_fails_for_a_successful_run()
    {
        var assertions = Create(RunStatus.Completed);

        Should.Throw<TraconAssertionException>(() => assertions.ShouldHaveFailed());
    }

    [Fact]
    public void ShouldHaveFailedWith_passes_for_the_correct_error_type()
    {
        var assertions = Create(RunStatus.Failed, error: new RunError { Type = "content_filtered", Message = "x" });

        Should.NotThrow(() => assertions.ShouldHaveFailedWith("content_filtered"));
    }

    [Fact]
    public void ShouldHaveFailedWith_fails_with_the_expected_and_actual_value_for_the_wrong_error_type()
    {
        var assertions = Create(RunStatus.Failed, error: new RunError { Type = "timeout", Message = "x" });

        var exception = Should.Throw<TraconAssertionException>(() => assertions.ShouldHaveFailedWith("content_filtered"));

        exception.Message.ShouldContain("content_filtered", Case.Sensitive);
        exception.Message.ShouldContain("timeout", Case.Sensitive);
    }

    [Fact]
    public void ShouldHaveOutputContaining_passes_when_the_text_is_present()
    {
        var assertions = Create(RunStatus.Completed, events:
        [
            Event(RunEventType.MessageCompleted, "refunded"),
        ]);

        Should.NotThrow(() => assertions.ShouldHaveOutputContaining("refunded"));
    }

    [Fact]
    public void ShouldHaveOutputContaining_reads_from_MessageDelta_chunks_in_a_streaming_run()
    {
        // MessageCompleted is only written on the non-streaming path
        // (RunRecordingAgent.RunCoreAsync); the HTTP /run endpoint streams and
        // only produces MessageDelta chunks.
        var assertions = Create(RunStatus.Completed, events:
        [
            Event(RunEventType.MessageDelta, "refun"),
            Event(RunEventType.MessageDelta, "ded"),
        ]);

        Should.NotThrow(() => assertions.ShouldHaveOutputContaining("refunded"));
    }

    [Fact]
    public void ShouldHaveOutputContaining_fails_with_the_actual_output_when_the_text_is_absent()
    {
        var assertions = Create(RunStatus.Completed, events:
        [
            Event(RunEventType.MessageCompleted, "a different response"),
        ]);

        var exception = Should.Throw<TraconAssertionException>(
            () => assertions.ShouldHaveOutputContaining("refunded"));

        exception.Message.ShouldContain("refunded", Case.Sensitive);
        exception.Message.ShouldContain("a different response", Case.Sensitive);
    }

    private static RunEvent Event(RunEventType type, string text)
        => new() { RunId = Guid.NewGuid(), Sequence = 0, Type = type, Timestamp = Now, Text = text };

    private static RunAssertions Create(
        RunStatus status,
        RunError? error = null,
        IReadOnlyList<RunEvent>? events = null,
        IReadOnlyList<ToolInvocationRecord>? toolInvocations = null)
    {
        var record = new RunRecord
        {
            Id = Guid.NewGuid(),
            AgentName = "test-agent",
            Status = status,
            StartedAt = Now,
            Error = error,
        };

        return new RunAssertions(record, events ?? [], toolInvocations ?? []);
    }
}
