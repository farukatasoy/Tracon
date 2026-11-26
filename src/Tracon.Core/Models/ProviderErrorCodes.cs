using System.Text.RegularExpressions;

namespace Tracon;

/// <summary>
/// Reads the machine-readable code a provider put in an error answer (<c>insufficient_quota</c>, <c>voice_not_found</c>), and nothing
/// else of what it said.
/// </summary>
/// <remarks>
/// <para>
/// The provider's own words can quote the request, an internal address or part of a credential, and a run record is permanent, so
/// none of them is kept. A code is different: it is a short token of a closed vocabulary the provider documents, and it is the
/// one thing a caller needs to tell a spent quota from a rate limit, a refused content from a bad parameter. It is accepted only in
/// the shape of such a token (<c>A-Z a-z 0-9 _ . -</c>, at most 64 characters), so a message that was put in the place of a code
/// is dropped and not carried.
/// </para>
/// <para>
/// Two sources. A Tracon adapter that read the body of an answer (the speech client, the OpenAI image generator) stores the code
/// on the exception it throws under <see cref="DataKey"/>. A provider SDK that writes the code into its own message
/// (<c>HTTP 404 (invalid_request_error: model_not_found)</c>, <c>HTTP 401 (invalid_api_key)</c>) is read as text, the way the
/// status is: this package knows no SDK exception type.
/// </para>
/// </remarks>
internal static partial class ProviderErrorCodes
{
    /// <summary>The key an adapter stores the code under in <see cref="Exception.Data"/>.</summary>
    internal const string DataKey = "tracon.provider-error-code";

    private const int MaxLength = 64;

    /// <summary>Reads the code from anywhere in an exception's graph.</summary>
    /// <param name="exception">The failure.</param>
    /// <returns>The code, or <see langword="null"/> when no frame carries one.</returns>
    internal static string? Read(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        foreach (var frame in ProviderFailureGraph.Frames(exception))
        {
            if (frame.Data.Contains(DataKey) && frame.Data[DataKey] is string stored && IsCode(stored))
            {
                return stored;
            }

            var match = MessageCodePattern().Match(frame.Message);

            if (match.Success)
            {
                // "type: code" names the code; "type: " (the answer carried none) and a single token name the one there is.
                var code = match.Groups["code"].Value is { Length: > 0 } second ? second : match.Groups["first"].Value;

                if (IsCode(code))
                {
                    return code;
                }
            }
        }

        return null;
    }

    /// <summary>Tells whether a text has the shape of a provider error code.</summary>
    /// <param name="value">The text.</param>
    /// <returns><see langword="true"/> for a token of at most 64 letters, digits, <c>_</c>, <c>.</c> and <c>-</c>.</returns>
    internal static bool IsCode(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!(char.IsAsciiLetterOrDigit(character) || character is '_' or '.' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Stores a code on an exception, when it has the shape of one.</summary>
    /// <param name="exception">The exception the adapter is about to throw.</param>
    /// <param name="code">The code the answer carried, or <see langword="null"/>.</param>
    internal static void Attach(Exception exception, string? code)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (IsCode(code))
        {
            exception.Data[DataKey] = code;
        }
    }

    // Measured against the OpenAI client library: "HTTP 400 (insufficient_quota: insufficient_quota)" is the type, a colon and the
    // code, and "HTTP 400 (server_error: )" an answer with no code. "HTTP 401 (invalid_api_key)" is a code alone. A reason phrase
    // ("HTTP 503 (Service Unavailable)") has a space and is not a code.
    [GeneratedRegex(
        @"\bHTTP\s+[1-5]\d{2}\s+\((?<first>[A-Za-z0-9_.-]{0,64})(?::\s*(?<code>[A-Za-z0-9_.-]{0,64}))?\)",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex MessageCodePattern();
}
