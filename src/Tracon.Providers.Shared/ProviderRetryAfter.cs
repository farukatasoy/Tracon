using System.Globalization;
using System.Net.Http.Headers;

namespace Tracon;

/// <summary>
/// The longest wait a provider's <c>Retry-After</c> may impose on a retry, and the reader that tells a usable
/// header from an absurd one.
/// </summary>
/// <remarks>
/// <para>
/// A provider that answers <c>429</c> or <c>503</c> says how long to wait before the same request is sent again.
/// The client libraries of two of the four providers obey the header without a ceiling: a value of one day held a
/// run for one day, and a value beyond about 49 days (an HTTP date in the next century counts) made the library's
/// own timer throw an <see cref="ArgumentOutOfRangeException"/> that ended the run as a failure of the wrong kind.
/// The header comes from the network, and a proxy, a misconfigured gateway or a hostile endpoint can write
/// anything into it.
/// </para>
/// <para>
/// The rule is the one the providers' own reference libraries apply: a delay up to <see cref="MaximumHonoredDelay"/>
/// is honored as asked, and a longer one is ignored, so the library falls back to its ordinary backoff and retries
/// a bounded number of times. A value that is not a delay at all is already ignored by the libraries.
/// </para>
/// <para>
/// Shared source: this type compiles into each provider assembly and stays <see langword="internal"/>
/// (<c>src/Tracon.Providers.Shared/README.md</c>). The Google library does not read the header and caps its own
/// backoff, so nothing there uses it.
/// </para>
/// </remarks>
internal static class ProviderRetryAfter
{
    /// <summary>The longest delay a <c>Retry-After</c> header is obeyed for. A longer one is ignored.</summary>
    internal static readonly TimeSpan MaximumHonoredDelay = TimeSpan.FromSeconds(60);

    private const string RetryAfterHeader = "Retry-After";
    private const string RetryAfterMsHeader = "Retry-After-Ms";

    /// <summary>
    /// Removes the retry headers of <paramref name="response"/> that ask for longer than
    /// <see cref="MaximumHonoredDelay"/>, so a library that reads them afterwards falls back to its own backoff.
    /// </summary>
    /// <param name="response">The provider's response. Its headers are changed in place.</param>
    /// <param name="now">The current time, for a header that carries a date.</param>
    /// <remarks>
    /// For a library that cannot be given a retry policy but accepts a message handler. A header that is not a
    /// delay, or asks for less than the ceiling, is left as it is.
    /// </remarks>
    internal static void RemoveExcessiveDelays(HttpResponseHeaders response, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (ExceedsMaximum(FirstValue(response, RetryAfterHeader), milliseconds: false, now))
        {
            response.Remove(RetryAfterHeader);
        }

        if (ExceedsMaximum(FirstValue(response, RetryAfterMsHeader), milliseconds: true, now))
        {
            response.Remove(RetryAfterMsHeader);
        }
    }

    /// <summary>Determines whether one header value asks for longer than <see cref="MaximumHonoredDelay"/>.</summary>
    /// <param name="value">The header value, or <see langword="null"/> when the header is absent.</param>
    /// <param name="milliseconds">Whether a number is milliseconds (<c>Retry-After-Ms</c>) instead of seconds.</param>
    /// <param name="now">The current time, for a value that is an HTTP date.</param>
    /// <returns>
    /// <see langword="true"/> when the value is a number or a date that stands for more than the ceiling, or a number
    /// too large to be one. <see langword="false"/> for an absent value, a value that is not a delay, and a delay within
    /// the ceiling.
    /// </returns>
    internal static bool ExceedsMaximum(string? value, bool milliseconds, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            var seconds = milliseconds ? number / 1000d : number;

            return !double.IsFinite(seconds) || seconds > MaximumHonoredDelay.TotalSeconds;
        }

        // Only the seconds form carries a date; Retry-After-Ms is a number or nothing.
        return !milliseconds
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            && date - now > MaximumHonoredDelay;
    }

    private static string? FirstValue(HttpResponseHeaders headers, string name)
        => headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
}
