using System.Net.Http.Headers;

namespace Tracon;

/// <summary>
/// The one rule every key Tracon sends to a provider at setup time passes before it becomes
/// a header: leading and trailing white space is removed, and a key that still cannot be
/// carried by an HTTP header is refused with a sentence that never repeats the key.
/// </summary>
/// <remarks>
/// <para>
/// A key read from a file or a mounted secret usually ends in a line break. Some SDKs
/// validate the header value they build and refuse it with the framework's own sentence
/// (<c>The format of value 'Bearer ...' is invalid</c>), and that sentence quotes the whole
/// value: the key then reaches the response of the run, the run record, the logs and the
/// chat span. A key set up at start has no per-call credential to mask, so the value is
/// made safe before a header is built from it, not rewritten afterwards. The other SDKs
/// write the value to the wire as it is, line break included, which is also wrong.
/// </para>
/// <para>
/// What is refused, after the trim: a control character (a line break, a NUL, a tab, DEL),
/// any character outside printable ASCII, and a quote or a comma the <c>Authorization</c>
/// header syntax does not accept. The last test asks the framework's own parser, the one
/// the strictest SDK uses, so the rule follows what the runtime accepts and not a list
/// written here.
/// </para>
/// <para>
/// Only the key set up at start goes through this type. A key a tenant supplies is
/// masked wherever the text of its failure is shown (<c>ProviderSecretMask</c>), and it is
/// sent as it was stored.
/// </para>
/// </remarks>
internal static class OutboundApiKey
{
    private const string Reason =
        "The key is sent in an HTTP header and cannot carry it. White space at either end is removed, so the character " +
        "is inside the key; check where the key is read from. The key is not repeated here.";

    /// <summary>Removes leading and trailing white space and checks what is left.</summary>
    /// <param name="optionName">The option the key came from, as it is written in the failure.</param>
    /// <param name="key">The key as configured; may be <see langword="null"/>.</param>
    /// <returns>
    /// The key to send, or <see langword="null"/> when nothing is left (missing, empty or
    /// white space only), so the caller keeps its own sentence for an empty key.
    /// </returns>
    /// <exception cref="TraconException">
    /// The key contains a character a header cannot carry. The message names
    /// <paramref name="optionName"/> and never contains the key.
    /// </exception>
    internal static string? Normalize(string optionName, string? key)
    {
        var trimmed = key?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return Refuse(optionName, trimmed) is { } refusal ? throw new TraconException(refusal) : trimmed;
    }

    /// <summary>Tells whether a configured key would be refused, for an options validator.</summary>
    /// <param name="optionName">The option the key came from, as it is written in the failure.</param>
    /// <param name="key">The key as configured; may be <see langword="null"/>.</param>
    /// <returns>
    /// The failure sentence, which never contains the key, or <see langword="null"/> when the
    /// key is usable or empty (the validator has its own sentence for an empty key).
    /// </returns>
    internal static string? Refusal(string optionName, string? key)
    {
        var trimmed = key?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : Refuse(optionName, trimmed);
    }

    private static string? Refuse(string optionName, string trimmed)
    {
        foreach (var character in trimmed)
        {
            if (char.IsControl(character))
            {
                return $"{optionName} contains a control character (a line break, a NUL, a tab or another character that does not print). " +
                    Reason;
            }

            if (character > '~')
            {
                return $"{optionName} contains a character outside printable ASCII. " + Reason;
            }
        }

        // The header syntax is the framework's to judge: a lone quote and a comma make its
        // parser refuse "Bearer <key>", which is the form the strictest SDK builds.
        return AuthenticationHeaderValue.TryParse("Bearer " + trimmed, out _)
            ? null
            : $"{optionName} contains a quote or a comma that the HTTP Authorization header syntax does not accept. " + Reason;
    }
}
