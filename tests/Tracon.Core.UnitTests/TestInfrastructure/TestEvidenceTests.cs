namespace Tracon.Core.UnitTests.TestInfrastructure;

/// <summary>
/// The notes a test leaves for the case that it fails (<c>tests/Shared/Evidence/TestEvidence.cs</c>).
/// </summary>
/// <remarks>
/// The hook that reads a test's result runs inside xunit; these cases pin what it does with a result. Whether xunit hands
/// the hook the result at all is proved by the self-test that fails on purpose in CI.
/// </remarks>
public sealed class TestEvidenceTests
{
    [Fact]
    public void A_test_that_failed_gets_its_notes_in_name_order()
    {
        var text = TestEvidence.Render(
            "Some.Test",
            TestResult.Failed,
            "it broke",
            [("zeta", _ => "last"), ("alpha", failure => $"first, after: {failure.Text}")]);

        text.ShouldNotBeNull();
        text.ShouldStartWith("[evidence] Some.Test");
        text.IndexOf("[alpha]", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("[zeta]", StringComparison.Ordinal));
        text.ShouldContain("first, after: it broke");
    }

    [Theory]
    [InlineData(TestResult.Passed)]
    [InlineData(TestResult.Skipped)]
    [InlineData(TestResult.NotRun)]
    [InlineData(null)]
    public void A_test_that_did_not_fail_calls_no_note_and_writes_nothing(TestResult? result)
    {
        var called = 0;

        var text = TestEvidence.Render(
            "Some.Test",
            result,
            string.Empty,
            [("note", _ =>
            {
                called++;

                return "unused";
            })]);

        text.ShouldBeNull();
        called.ShouldBe(0);
    }

    [Fact]
    public void A_note_that_throws_becomes_a_line_and_does_not_hide_the_others()
    {
        var text = TestEvidence.Render(
            "Some.Test",
            TestResult.Failed,
            "it broke",
            [("broken", _ => throw new InvalidOperationException("disposed")), ("fine", _ => "still here")]);

        text.ShouldNotBeNull();
        text.ShouldContain("the note could not be read: InvalidOperationException: disposed");
        text.ShouldContain("still here");
    }

    [Fact]
    public void A_note_with_nothing_to_say_leaves_no_section()
    {
        var text = TestEvidence.Render("Some.Test", TestResult.Failed, "it broke", [("empty", _ => null), ("blank", _ => "  ")]);

        text.ShouldNotBeNull();
        text.ShouldNotContain("[empty]");
        text.ShouldNotContain("[blank]");
    }

    [Fact]
    public void The_reference_ids_of_a_failure_are_found_once_each()
    {
        TestEvidence.References("JobStatus.Failed (ref: 234bf944) then again (ref: 234bf944) and (ref: ab-12)")
            .ShouldBe(["234bf944", "ab-12"]);

        TestEvidence.References("nothing to find here").ShouldBeEmpty();
    }

    [Fact]
    public void A_note_registered_by_this_test_is_stored_under_this_tests_own_key()
    {
        TestEvidence.Register("mine", _ => "mine");

        var own = TestContext.Current.Test!.UniqueID;

        TestContext.Current.KeyValueStorage.Keys
            .Count(key => key.Contains(own, StringComparison.Ordinal) && key.EndsWith(":mine", StringComparison.Ordinal))
            .ShouldBe(1);
    }
}
