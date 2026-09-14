using Tracon.CapacityDriver;

namespace Tracon.Capacity.Tests;

/// <summary>The scan every artifact passes through before it is kept.</summary>
/// <remarks>
/// The capacity run holds a live connection string for its whole duration and
/// starts processes whose environment carries it. These cases use synthetic
/// canary credentials, never a real one.
/// </remarks>
public sealed class CapacityRedactionTests
{
    // 🚨 Every value below is SYNTHETIC-CREDENTIAL: invented for this test,
    // connected to nothing. The repository's own secret gate skips a line
    // carrying that marker and reports how many it skipped, so the exception
    // stays visible in review rather than hiding in an allowlist.
    [Theory]
    [InlineData("Host=db;Username=capacity;Password=hunter2secret;Database=x")] // SYNTHETIC-CREDENTIAL
    [InlineData("Server=db;pwd=hunter2secret")] // SYNTHETIC-CREDENTIAL
    [InlineData("postgres://capacity:hunter2secret@localhost:5432/x")] // SYNTHETIC-CREDENTIAL
    [InlineData("sk-canary-AAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // SYNTHETIC-CREDENTIAL
    [InlineData("AVNS_CANARY01234567")] // SYNTHETIC-CREDENTIAL
    public void A_credential_shape_is_found_and_scrubbed(string text)
    {
        Redactor.ContainsSecret(text).ShouldBeTrue();

        var scrubbed = Redactor.Scrub(text);

        scrubbed.ShouldContain(Redactor.Placeholder);
        scrubbed.ShouldNotContain("hunter2secret", Case.Sensitive);
        scrubbed.ShouldNotContain("CANARY01234567", Case.Sensitive);
        Redactor.ContainsSecret(scrubbed).ShouldBeFalse();
    }

    [Fact]
    public void A_connection_string_without_a_credential_survives_intact()
    {
        const string Text = "Host=127.0.0.1;Port=5432;Database=capacity;Username=capacity";

        Redactor.ContainsSecret(Text).ShouldBeFalse();
        Redactor.Scrub(Text).ShouldBe(Text);
    }

    [Fact]
    public void An_ordinary_report_sentence_is_not_mangled()
    {
        const string Text = "p95 latency was 1204 ms at concurrency 32; no ceiling was found.";

        Redactor.Scrub(Text).ShouldBe(Text);
    }

    [Fact]
    public void Null_and_empty_are_handled_rather_than_throwing()
    {
        Redactor.Scrub(null).ShouldBe("");
        Redactor.ContainsSecret(null).ShouldBeFalse();
        Redactor.ContainsSecret("").ShouldBeFalse();
    }
}
