using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// What a failed test keeps of its host's log (<see cref="TraconTestHost.FailureNote"/>).
/// </summary>
public sealed class HostFailureEvidenceTests
{
    private const string Reference = "5e1f7a02";

    [Fact]
    public void The_note_holds_warnings_and_errors_and_the_lines_the_failure_points_at()
    {
        string[] entries =
        [
            "Information Tracon.Startup the host started ",
            $"Information Tracon.Jobs job 'a' failed after the third attempt (ref: {Reference}) ",
            "Information Tracon.Jobs job 'b' finished ",
            "Warning Tracon.Quota the budget is nearly spent ",
            "Error Tracon.Jobs the store did not answer System.TimeoutException",
            "Critical Tracon.Host the host is stopping ",
        ];

        var note = TraconTestHost.FailureNote(entries, $"JobStatus.Failed (ref: {Reference})");

        note.ShouldNotBeNull();
        note.ShouldContain($"(ref: {Reference})");
        note.ShouldContain("Warning Tracon.Quota");
        note.ShouldContain("Error Tracon.Jobs");
        note.ShouldContain("Critical Tracon.Host");
        note.ShouldNotContain("the host started");
        note.ShouldNotContain("job 'b' finished");
    }

    [Fact]
    public void A_failure_without_a_reference_keeps_only_the_serious_lines()
    {
        var note = TraconTestHost.FailureNote(
            ["Information Tracon.Jobs job finished (ref: abc123) ", "Warning Tracon.Quota near the limit "],
            "Expected 1 but was 2");

        note.ShouldNotBeNull();
        note.ShouldContain("near the limit");
        note.ShouldNotContain("abc123");
    }

    [Fact]
    public void A_log_with_nothing_to_say_leaves_no_note()
        => TraconTestHost.FailureNote(["Information Tracon.Startup started "], "Expected 1 but was 2").ShouldBeNull();

    [Fact]
    public void The_note_is_bounded_in_lines_and_says_how_many_it_left_out()
    {
        var entries = Enumerable.Range(0, TraconTestHost.NoteLineLimit + 30).Select(index => $"Warning Tracon.Test line {index} ");

        var note = TraconTestHost.FailureNote(entries, string.Empty);

        note.ShouldNotBeNull();
        note.ShouldContain("30 more lines left out");
        note.ShouldContain($"line {TraconTestHost.NoteLineLimit - 1} ");
        note.ShouldNotContain($"line {TraconTestHost.NoteLineLimit} ");
    }

    [Fact]
    public void A_very_long_line_is_cut()
    {
        var note = TraconTestHost.FailureNote([$"Error Tracon.Test {new string('x', 10_000)}"], string.Empty);

        note.ShouldNotBeNull();
        note.Length.ShouldBeLessThan(TraconTestHost.NoteLineWidth + 100);
        note.ShouldContain("...");
    }

    [Fact]
    public async Task A_started_host_registers_its_log_as_a_note_for_the_running_test()
    {
        await using var host = await TraconTestHost.StartAsync();

        var own = TestContext.Current.Test!.UniqueID;

        TestContext.Current.KeyValueStorage.Keys
            .Count(key => key.Contains(own, StringComparison.Ordinal) && key.EndsWith(":host log", StringComparison.Ordinal))
            .ShouldBe(1);
    }

    /// <summary>
    /// Fails on purpose. CI runs it with the evidence variable set, expects red, and looks for the warning and the referenced
    /// line in this test's evidence file: they are there only if the note ran after the failure.
    /// </summary>
    [Fact]
    public async Task A_failed_job_leaves_the_line_its_reference_points_at()
    {
        EvidenceSelfTest.SkipUnlessEnabled();

        await using var host = await TraconTestHost.StartAsync();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Tracon.SelfTest");

        logger.LogInformation("an unrelated line the note must leave out");
        logger.LogInformation("job 'self-test' failed after the third attempt (ref: 5e1f7a02)");
        logger.LogWarning("a warning the note must keep");

        throw new InvalidOperationException($"JobStatus.Failed (ref: {Reference})");
    }
}
