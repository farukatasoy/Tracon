using System.Text.RegularExpressions;

namespace Tracon.CapacityDriver;

/// <summary>Finds credential-shaped text in anything the apparatus is about to write.</summary>
/// <remarks>
/// <para>
/// 🚨 The capacity run holds a live connection string and starts processes
/// whose environment carries it. Every artifact - manifest, summary, report,
/// captured stdout and stderr - passes through here before it is kept, and the
/// acceptance suite plants a synthetic canary credential to prove the scan
/// actually looks.
/// </para>
/// <para>
/// The patterns match the repository's own secret gate
/// (<c>scripts/kapi.py</c>) plus the two shapes this apparatus can produce on
/// its own: a PostgreSQL connection string and a URI with inline credentials.
/// </para>
/// </remarks>
public static partial class Redactor
{
    /// <summary>The text written in place of a match.</summary>
    public const string Placeholder = "[redacted]";

    [GeneratedRegex(@"(?i)(password|pwd)\s*=\s*[^\s"";]+", RegexOptions.CultureInvariant)]
    private static partial Regex PasswordAssignment();

    [GeneratedRegex(@"(?i)\b[a-z][a-z0-9+.-]*://[^\s/@:]+:[^\s/@]+@", RegexOptions.CultureInvariant)]
    private static partial Regex UriCredential();

    [GeneratedRegex(@"sk-[a-z]+-[A-Za-z0-9_-]{24,}", RegexOptions.CultureInvariant)]
    private static partial Regex ProviderKey();

    [GeneratedRegex(@"AVNS_[A-Za-z0-9]{12,}", RegexOptions.CultureInvariant)]
    private static partial Regex ManagedDatabaseKey();

    /// <summary>Replaces every credential-shaped run of text.</summary>
    /// <param name="text">The text to clean.</param>
    /// <returns>The cleaned text.</returns>
    public static string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? "";
        }

        var scrubbed = PasswordAssignment().Replace(text, Placeholder);
        scrubbed = UriCredential().Replace(scrubbed, match => match.Value[..(match.Value.IndexOf("//", StringComparison.Ordinal) + 2)] + Placeholder + "@");
        scrubbed = ProviderKey().Replace(scrubbed, Placeholder);
        scrubbed = ManagedDatabaseKey().Replace(scrubbed, Placeholder);
        return scrubbed;
    }

    /// <summary>Whether the text still holds something credential-shaped.</summary>
    /// <param name="text">The text to inspect.</param>
    /// <returns><see langword="true"/> when a credential shape is present.</returns>
    public static bool ContainsSecret(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        return PasswordAssignment().IsMatch(text)
            || UriCredential().IsMatch(text)
            || ProviderKey().IsMatch(text)
            || ManagedDatabaseKey().IsMatch(text);
    }
}
