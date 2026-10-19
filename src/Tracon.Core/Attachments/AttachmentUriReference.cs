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
internal static class AttachmentUriReference
{
    private const string Marker = "/api/attachments/";

    /// <summary>
    /// The reference's property that carries the embedded file's name to
    /// <see cref="Microsoft.Extensions.AI.DataContent.Name"/>; a <see cref="Microsoft.Extensions.AI.UriContent"/>
    /// has no name of its own. It never reaches a provider: resolution moves it back.
    /// </summary>
    public const string NameProperty = "tracon.attachment_name";

    /// <summary>
    /// The properties a reference keeps from the embedded content it replaces:
    /// the content's own properties (an image's <c>detail</c>) and its name.
    /// </summary>
    /// <param name="data">The embedded content.</param>
    /// <returns>A copy of the properties, or <see langword="null"/> if there are none.</returns>
    public static Microsoft.Extensions.AI.AdditionalPropertiesDictionary? CarryProperties(Microsoft.Extensions.AI.DataContent data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var properties = data.AdditionalProperties?.Clone();

        if (data.Name is { Length: > 0 } name)
        {
            (properties ??= [])[NameProperty] = name;
        }

        return properties is { Count: > 0 } ? properties : null;
    }

    /// <summary>
    /// Builds the content a reference resolves to: the bytes under the
    /// reference's media type, with the name and the properties the reference
    /// carried (<see cref="CarryProperties"/>).
    /// </summary>
    /// <param name="reference">The reference.</param>
    /// <param name="bytes">The attachment's bytes.</param>
    /// <returns>The resolved content.</returns>
    public static Microsoft.Extensions.AI.DataContent Resolve(Microsoft.Extensions.AI.UriContent reference, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var resolved = new Microsoft.Extensions.AI.DataContent(bytes, reference.MediaType);

        if (reference.AdditionalProperties is not { Count: > 0 } carried)
        {
            return resolved;
        }

        var properties = carried.Clone();

        if (properties.TryGetValue(NameProperty, out var name) && name is string fileName)
        {
            resolved.Name = fileName;
            properties.Remove(NameProperty);
        }

        resolved.AdditionalProperties = properties.Count > 0 ? properties : null;
        return resolved;
    }

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
