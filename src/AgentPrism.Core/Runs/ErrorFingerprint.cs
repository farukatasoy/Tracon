using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AgentPrism;

/// <summary>
/// Turns an error message into a clustering fingerprint.
/// </summary>
/// <remarks>
/// <para>
/// A raw message carries variable parts such as an identity, a number, or a
/// timestamp; each one would otherwise form its own cluster. The
/// normalization order (identity → number → timestamp) is deliberate: the
/// timestamp pattern runs AFTER the number replacement and captures the
/// ISO-8601-shaped sequence of <c>{n}</c> tokens that replaced the numbers —
/// it does not need a separate date-validation pattern of its own.
/// </para>
/// <para>
/// Quoted text is DELIBERATELY not stripped: a tool name
/// is distinguishing, and stripping it would merge two different tool errors
/// into a single cluster. Identity/number/date cleanup already removes most of
/// the noise from messages.
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

        var normalized = GuidPattern().Replace(message, "{guid}");
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

    [GeneratedRegex(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex GuidPattern();

    [GeneratedRegex(@"\d+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
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
