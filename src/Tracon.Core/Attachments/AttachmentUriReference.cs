namespace Tracon;

/// <summary>
/// Converts an attachment to a <see cref="Microsoft.Extensions.AI.UriContent"/>
/// reference and resolves that reference back.
/// </summary>
/// <remarks>
/// Recognition only checks the <c>/api/attachments/{id}</c> route. It does not need
/// to know the path prefix, <c>{prefix}</c>, and recognizes relative and absolute URIs.
/// This keeps <c>Tracon.Core</c> fully independent from endpoint configuration.
/// </remarks>
public static class AttachmentUriReference
{
    private const string Marker = "/api/attachments/";

    /// <summary>Converts an attachment identifier to its reference URI.</summary>
    /// <param name="prefix">The path prefix for Tracon endpoints, for example <c>/tracon</c>.</param>
    /// <param name="attachmentId">The attachment identifier.</param>
    /// <returns>A relative URI.</returns>
    public static Uri Create(string prefix, Guid attachmentId)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        return new Uri($"{prefix.TrimEnd('/')}{Marker}{attachmentId}", UriKind.Relative);
    }

    /// <summary>Determines whether a URI is an attachment reference and extracts its identifier.</summary>
    /// <param name="uri">The URI to inspect.</param>
    /// <param name="attachmentId">The attachment identifier when found.</param>
    /// <returns><see langword="true"/> if the URI is an attachment reference.</returns>
    public static bool TryParse(Uri uri, out Guid attachmentId)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var path = uri.IsAbsoluteUri ? uri.AbsolutePath : uri.OriginalString;
        var index = path.IndexOf(Marker, StringComparison.Ordinal);

        if (index < 0)
        {
            attachmentId = default;
            return false;
        }

        var idPart = path[(index + Marker.Length)..].TrimEnd('/');
        return Guid.TryParse(idPart, out attachmentId);
    }
}
