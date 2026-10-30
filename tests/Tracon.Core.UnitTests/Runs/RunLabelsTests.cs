namespace Tracon.Core.UnitTests.Runs;

/// <summary>
/// The message that rejects a run's labels is text a caller can read.
/// </summary>
public sealed class RunLabelsTests
{
    [Fact]
    public void A_key_that_is_too_long_is_echoed_cut_but_never_inside_a_surrogate_pair()
    {
        // Character 64 is the high half of a pair. The message goes into a problem-details body;
        // a lone surrogate in it cannot be written as JSON, so the 400 became a 500.
        var key = new string('k', RunLabels.MaxKeyLength - 1) + "\U0001F600" + "tail";

        var message = RunLabels.ValidateLabels(new Dictionary<string, string>(StringComparer.Ordinal) { [key] = "v" });

        message.ShouldNotBeNull();
        message.ShouldContain(new string('k', RunLabels.MaxKeyLength - 1) + "...", Case.Sensitive);
        ContentEventPayload.Sanitize(message).ShouldBeSameAs(message);
    }
}
