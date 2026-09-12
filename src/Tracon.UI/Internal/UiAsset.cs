namespace Tracon;

/// <summary>
/// Represents the definition of an embedded UI asset.
/// </summary>
/// <remarks>
/// Because the content is produced at build time and never changes afterward, all
/// fields are immutable; a single instance is shared across all requests.
/// </remarks>
internal sealed class UiAsset
{
    /// <summary>Gets the embedded resource name of the asset.</summary>
    public required string ResourceName { get; init; }

    /// <summary>Gets the asset path relative to the base path. Example: <c>assets/index-a1b2c3.js</c>.</summary>
    public required string Path { get; init; }

    /// <summary>Gets the content type.</summary>
    public required string ContentType { get; init; }

    /// <summary>Gets whether the content is stored Brotli-compressed.</summary>
    public required bool IsBrotli { get; init; }

    /// <summary>
    /// Gets whether the file name carries a content hash. If it does, the response can be
    /// cached forever.
    /// </summary>
    /// <remarks>
    /// Vite appends a content hash to every file under <c>assets/</c>; when the content
    /// changes, the name changes too. That is why those files are marked <c>immutable</c>.
    /// <c>index.html</c> has a fixed name and is always revalidated.
    /// </remarks>
    public required bool Immutable { get; init; }
}
