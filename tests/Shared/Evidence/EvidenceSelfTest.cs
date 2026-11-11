namespace Tracon.Tests.Common;

/// <summary>
/// The switch for the tests that fail on purpose, so the evidence pipeline is proved by a real failure and not assumed.
/// </summary>
/// <remarks>
/// <para>
/// Evidence that nobody has seen arrive is a claim. CI runs the three tests that carry <see cref="SkipUnlessEnabled"/> with
/// <see cref="Variable"/> set, expects them to be red, and looks for the heartbeat file, the host log note and the browser
/// trace they were meant to leave. Without the variable they are skipped, so a normal run never goes red because of them.
/// </para>
/// </remarks>
internal static class EvidenceSelfTest
{
    /// <summary>The environment variable that turns the failing tests on.</summary>
    internal const string Variable = "TRACON_EVIDENCE_SELFTEST";

    /// <summary>Skips the calling test unless <see cref="Variable"/> is <c>1</c>.</summary>
    internal static void SkipUnlessEnabled()
        => Assert.SkipUnless(
            string.Equals(Environment.GetEnvironmentVariable(Variable), "1", StringComparison.Ordinal),
            $"{Variable}=1 turns on the tests that fail on purpose to prove the failure evidence is written.");
}
