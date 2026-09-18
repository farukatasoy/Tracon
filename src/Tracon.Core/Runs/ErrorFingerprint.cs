using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Tracon;

/// <summary>
/// Turns an error message into a clustering fingerprint.
/// </summary>
/// <remarks>
/// <para>
/// A raw message carries variable parts such as an identity, a number, or a
/// timestamp; each one would otherwise form its own cluster. The
/// normalization order (correlation id → identity → number → timestamp) is
/// deliberate at both ends: the correlation id is replaced FIRST, before the
/// number pattern can chew it into a per-occurrence shape, and the timestamp
/// pattern runs AFTER the number replacement and captures the ISO-8601-shaped
/// sequence of <c>{n}</c> tokens that replaced the numbers — it does not need a
/// separate date-validation pattern of its own.
/// </para>
/// <para>
/// Quoted text is DELIBERATELY not stripped: a tool name
/// is distinguishing, and stripping it would merge two different tool errors
/// into a single cluster. Identity/number/date cleanup already removes most of
/// the noise from messages.
/// </para>
/// <para>
/// An HTTP status is the one number that is NOT noise, and it is kept. The
/// number pattern used to swallow it, so a provider's 404 and its 500 produced
/// the same cluster key — the number cleanup exists to stop an id or a count
/// from splitting one fault into many, and a status code does the opposite: it
/// is what tells two faults apart. Measured on a real OpenAI 404 and a real
/// OpenRouter 402, which shared one fingerprint character for character.
/// </para>
/// </remarks>
internal static partial class ErrorFingerprint
{
    /// <summary>
    /// The maximum length of the normalized text before hashing. Very long
    /// messages (such as a stack trace) are not hashed in full; clustering
    /// relies on the meaningful part at the start of the message.
    /// </summary>
    private const int MaxNormalizedLength = 500;

    /// <summary>
    /// Normalizes an error message and returns its SHA-256 digest as a
    /// lowercase hexadecimal string.
    /// </summary>
    /// <param name="message">The raw error message.</param>
    /// <returns>The lowercase hexadecimal SHA-256 digest.</returns>
    public static string Compute(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var normalized = CorrelationRefPattern().Replace(message, "(ref: {ref})");
        normalized = GuidPattern().Replace(normalized, "{guid}");
        normalized = NumberPattern().Replace(normalized, "{n}");
        normalized = TimestampPattern().Replace(normalized, "{ts}");
        normalized = DateOnlyPattern().Replace(normalized, "{ts}");

        if (normalized.Length > MaxNormalizedLength)
        {
            normalized = normalized[..MaxNormalizedLength];
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));

        // Convert.ToHexStringLower was added in net9.0; the repo also targets net8.0.
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // Runs BEFORE every other pattern. SafeErrorText.ForPersistence tags a
    // foreign exception's safe text with a correlation id that is new on each
    // occurrence, and GuidPattern cannot see it: NewCorrelationId formats with
    // "N", so the id carries no dashes. NumberPattern would then replace only
    // its digit runs and leave the hex letters standing ("a1b2c3d4" becomes
    // "a{n}b{n}c{n}d{n}"), giving the SAME failure a different cluster key every
    // time it happened — measured at 1368 clusters for 2000 occurrences of one
    // error. The length is deliberately not pinned to eight, so changing the id
    // does not quietly bring the split back.
    [GeneratedRegex(@"\(ref: [0-9a-fA-F]+\)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CorrelationRefPattern();

    [GeneratedRegex(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex GuidPattern();

    // The lookbehind keeps a status code that an "HTTP " prefix marks as one.
    // Everything else a message counts - an attempt number, a byte size, a line
    // number - still collapses to {n}, which is what stops one fault from
    // splitting into a cluster per occurrence.
    [GeneratedRegex(
        @"(?<!\bHTTP\s)\d+",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex NumberPattern();

    // Runs AFTER the number replacement: "2026-08-06T10:15:30.123Z" becomes
    // "{n}-{n}-{n}T{n}:{n}:{n}.{n}Z" in the previous step, and this pattern
    // collapses it into a single {ts}.
    [GeneratedRegex(
        @"\{n\}-\{n\}-\{n\}T\{n\}:\{n\}:\{n\}(?:\.\{n\})?Z?",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex TimestampPattern();

    [GeneratedRegex(@"\{n\}-\{n\}-\{n\}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DateOnlyPattern();
}
