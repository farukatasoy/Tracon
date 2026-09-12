namespace Tracon;

/// <summary>Produces the clustering digest Tracon's built-in classifier uses.</summary>
/// <remarks>
/// A thin public facade over <see cref="ErrorFingerprint"/>: the internal
/// type stays <c>partial</c> and keeps its <c>GeneratedRegex</c> members,
/// only this single entry point is a contract. A consumer composing
/// <see cref="DefaultRunErrorClassifier"/> into their own
/// <see cref="IRunErrorClassifier"/> uses this to produce a fingerprint that
/// lands in the SAME cluster as one Tracon computed —
/// <see cref="RunErrorClassification.Fingerprint"/> is otherwise unreachable.
/// </remarks>
public static class RunErrorFingerprint
{
    /// <summary>
    /// Normalizes an error message and returns its clustering digest.
    /// </summary>
    /// <param name="message">The raw error message. <see langword="null"/> is treated as empty.</param>
    /// <returns>The lowercase hexadecimal SHA-256 digest.</returns>
    public static string Compute(string? message) => ErrorFingerprint.Compute(message ?? string.Empty);
}
