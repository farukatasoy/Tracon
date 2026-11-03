using System.Text.Encodings.Web;
using System.Text.Json;

namespace Tracon;

/// <summary>
/// Replaces the credential a model call was made with wherever the text of that call's failure quotes it back.
/// </summary>
/// <remarks>
/// <para>
/// A provider that refuses a key often says which key it refused, and an SDK puts the provider's body into the
/// exception it throws. A logger that writes the exception, inner exceptions included, then writes the credential, and
/// so does a span whose status description is the exception's message.
/// </para>
/// <para>
/// The match is exact. The mask holds the values of ONE call's credential and replaces those values, in the three
/// shapes a body carries them (as they are, escaped for JSON, escaped for a URL). It looks for no pattern of a key:
/// a pattern also rewrites ordinary text, and it cannot know a provider's key format. What follows from that is
/// stated, not hidden. A provider that quotes only part of the key (a few leading and trailing characters) leaves that
/// part as it is, and a credential shorter than <see cref="MinimumValueLength"/> characters is not masked, because a
/// short value is also ordinary text.
/// </para>
/// <para>
/// The mask is for display. The raw exception graph stays whole where a decision reads it (the retry classifier, the
/// circuit breaker, the status reader), and only the text a logger or a span takes from an exception passes through
/// here. A mask never fails: when it cannot produce the masked text it returns a sentence that names the exception type
/// and nothing else, never the raw text.
/// </para>
/// <para>
/// A mask belongs to the pipeline built for one credential. It is immutable, and a pipeline of another tenant has its
/// own: one tenant's value never changes another tenant's text.
/// </para>
/// </remarks>
internal sealed class ProviderSecretMask
{
    /// <summary>The text that replaces a credential.</summary>
    /// <remarks>The same marker <see cref="ModelProviderCredential.ToString"/> uses.</remarks>
    internal const string Marker = "[redacted]";

    /// <summary>The shortest value that is masked.</summary>
    internal const int MinimumValueLength = 8;

    private readonly string[] _needles;

    private ProviderSecretMask(string[] needles)
    {
        _needles = needles;
    }

    /// <summary>Gets the mask that replaces nothing.</summary>
    internal static ProviderSecretMask Empty { get; } = new([]);

    /// <summary>Gets a value indicating whether this mask replaces nothing.</summary>
    internal bool IsEmpty => _needles.Length == 0;

    /// <summary>Builds the mask of one call's credential.</summary>
    /// <param name="credential">The credential the client was built with, or <see langword="null"/> for the setup-time key.</param>
    /// <returns>The mask; <see cref="Empty"/> when there is no credential value long enough to mask.</returns>
    internal static ProviderSecretMask For(ModelProviderCredential? credential)
        => credential is null ? Empty : ForValue(credential.ApiKey);

    /// <summary>Builds the mask of one value.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The mask; <see cref="Empty"/> when the value is missing or shorter than <see cref="MinimumValueLength"/>.</returns>
    internal static ProviderSecretMask ForValue(string? value)
    {
        if (value is null || value.Length < MinimumValueLength)
        {
            return Empty;
        }

        // A set, longest first: a shape that contains another is replaced before the shorter one can split it.
        var shapes = new HashSet<string>(StringComparer.Ordinal) { value };

        AddShape(shapes, value, static v => JsonEncodedText.Encode(v).Value);
        AddShape(shapes, value, static v => JsonEncodedText.Encode(v, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).Value);
        AddShape(shapes, value, static v => JsonEncodedText.Encode(v, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).Value.Replace("/", "\\/", StringComparison.Ordinal));
        AddShape(shapes, value, static v => Uri.EscapeDataString(v));
        AddShape(shapes, value, static v => System.Net.WebUtility.UrlEncode(v));

        return new([.. shapes.OrderByDescending(static shape => shape.Length).ThenBy(static shape => shape, StringComparer.Ordinal)]);
    }

    // A value the encoders refuse (an unpaired surrogate) has no escaped form; the raw form is still replaced. The mask is
    // built while a client is built, outside any handler of a provider failure, so it must not throw.
    private static void AddShape(HashSet<string> shapes, string value, Func<string, string> encode)
    {
        try
        {
            shapes.Add(encode(value));
        }
        catch (ArgumentException)
        {
        }
        catch (UriFormatException)
        {
        }
    }

    /// <summary>Replaces every value of the credential in <paramref name="text"/>.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The text with each value replaced by <see cref="Marker"/>.</returns>
    internal string Apply(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (_needles.Length == 0 || text.Length == 0)
        {
            return text;
        }

        var masked = text;

        foreach (var needle in _needles)
        {
            masked = masked.Replace(needle, Marker, StringComparison.Ordinal);
        }

        return masked;
    }

    /// <summary>Reads the message of <paramref name="exception"/>, masked.</summary>
    /// <param name="exception">The exception.</param>
    /// <returns>The masked message, or the sentence that names the type when the message cannot be read.</returns>
    internal string MessageOf(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        try
        {
            return Apply(exception.Message);
        }
        catch (Exception)
        {
            return Withheld(exception);
        }
    }

    /// <summary>Reads the full text of <paramref name="exception"/> (message, inner exceptions and stack), masked.</summary>
    /// <param name="exception">The exception.</param>
    /// <returns>The masked text, or the sentence that names the type when the text cannot be produced.</returns>
    internal string TextOf(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        try
        {
            return Apply(exception.ToString());
        }
        catch (Exception)
        {
            return Withheld(exception);
        }
    }

    /// <summary>
    /// Builds an exception that carries the masked text of <paramref name="exception"/> and nothing of its graph.
    /// </summary>
    /// <param name="exception">The exception a logger would have written.</param>
    /// <returns>
    /// An exception whose message and <see cref="object.ToString"/> are masked. It has no inner exception, so a writer that
    /// walks the graph reaches no raw frame.
    /// </returns>
    /// <remarks>
    /// The text keeps the type names, the messages and the stack of every frame, with the credential replaced, so an
    /// operator reads the whole failure. Only the credential is gone.
    /// </remarks>
    internal Exception View(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return new MaskedFailureView(exception.GetType().FullName ?? exception.GetType().Name, MessageOf(exception), TextOf(exception));
    }

    /// <summary>Names an exception and says its text was not written.</summary>
    /// <param name="exception">The exception whose text could not be masked.</param>
    /// <returns>The sentence.</returns>
    internal static string Withheld(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return $"{exception.GetType().FullName}: the text of this failure was withheld because it could not be masked.";
    }
}

/// <summary>A failure as an operator reads it, with the call's credential replaced and no graph behind it.</summary>
internal sealed class MaskedFailureView : Exception
{
    private readonly string _text;

    /// <summary>Creates a view.</summary>
    /// <param name="originalTypeName">The full name of the exception type this view stands for.</param>
    /// <param name="message">The masked message.</param>
    /// <param name="text">The masked full text.</param>
    internal MaskedFailureView(string originalTypeName, string message, string text)
        : base(message)
    {
        OriginalTypeName = originalTypeName;
        _text = text;
    }

    /// <summary>Gets the full name of the exception type this view stands for.</summary>
    internal string OriginalTypeName { get; }

    /// <inheritdoc />
    public override string ToString() => _text;
}
