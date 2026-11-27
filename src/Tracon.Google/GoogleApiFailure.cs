using System.Net;
using Google.GenAI;

namespace Tracon;

/// <summary>
/// Turns the error the Google client throws for an HTTP error answer into the failure the model pipeline reads a status from.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ApiException"/> derives from <see cref="HttpRequestException"/> but declares its own <c>StatusCode</c> with
/// <c>new</c>, so the status the base class reports stays empty. Core reads that base property, then the text of the message,
/// and knows no provider SDK type. A JSON error body makes the SDK write the service's own sentence as the message, and that
/// sentence holds no status: a <c>503</c> read as a failure that never got an answer, and a <c>400</c> as one worth another try.
/// </para>
/// <para>
/// The translated failure is a plain <see cref="HttpRequestException"/> with the status set, the message unchanged and the
/// original error as its inner exception, so a log still shows what the SDK threw. The status name the service gave
/// (<c>UNAVAILABLE</c>, <c>RESOURCE_EXHAUSTED</c>) becomes the provider error code.
/// </para>
/// </remarks>
internal static class GoogleApiFailure
{
    private const int MaxStatusNameLength = 40;

    /// <summary>Tells whether an error carries an HTTP status to translate.</summary>
    /// <param name="exception">The error the Google client threw.</param>
    /// <returns>
    /// <see langword="true"/> when the status is in the range of HTTP statuses. An error the SDK raised without a status
    /// (the status stays zero) is left as it was thrown: nothing is invented.
    /// </returns>
    internal static bool HasStatus(ApiException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.StatusCode is >= 100 and <= 599;
    }

    /// <summary>Builds the failure the model pipeline reads the status from.</summary>
    /// <param name="exception">An error for which <see cref="HasStatus"/> is <see langword="true"/>.</param>
    /// <returns>An <see cref="HttpRequestException"/> that carries the status and has <paramref name="exception"/> as its inner exception.</returns>
    internal static HttpRequestException Translate(ApiException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var translated = new HttpRequestException(exception.Message, exception, (HttpStatusCode)exception.StatusCode);

        ProviderErrorCodes.Attach(translated, StatusName(exception.Status));

        return translated;
    }

    /// <summary>Reads the status name the service gave (<c>INVALID_ARGUMENT</c>, <c>UNAVAILABLE</c>).</summary>
    /// <param name="status">The <see cref="ApiException.Status"/> of an error.</param>
    /// <returns>The name, or <see langword="null"/> when <paramref name="status"/> is not one.</returns>
    /// <remarks>
    /// The service's status name is a closed vocabulary of upper-case words. With an empty error body the SDK puts the
    /// HTTP reason phrase there instead (<c>Forbidden</c>, <c>Service Unavailable</c>), and a reason phrase is not a name
    /// the service gave. The chat and image translation and the speech failure text read the name through this one rule.
    /// </remarks>
    internal static string? StatusName(string? status)
        => status is { Length: > 0 and <= MaxStatusNameLength } && status.All(static c => c is (>= 'A' and <= 'Z') or '_')
            ? status
            : null;
}
