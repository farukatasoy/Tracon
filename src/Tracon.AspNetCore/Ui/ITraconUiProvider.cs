using Microsoft.AspNetCore.Http;

namespace Tracon;

/// <summary>
/// Source that serves the management UI's static assets.
/// </summary>
/// <remarks>
/// <para>
/// This abstraction is deliberately <strong>single-method</strong>. The asset
/// list, content type, <c>ETag</c>, cache headers, compression format, and
/// single-page-application fallback all belong entirely to the implementation.
/// This way, the HTTP layer's public API stays unchanged when the UI package
/// changes its own bundling format.
/// </para>
/// <para>
/// The dependency direction is <c>Tracon.UI → Tracon.AspNetCore</c> and
/// cannot be reversed. This is why <c>MapTracon</c> cannot call the UI
/// package directly; it resolves the registration from the service provider. If
/// no registration exists, the UI routes are never wired up and the HTTP surface
/// is unchanged.
/// </para>
/// <para>
/// Implementations are registered with <c>TryAdd</c>; the consumer's own
/// registration wins (the replaceable-extension rule).
/// </para>
/// </remarks>
public interface ITraconUiProvider
{
    /// <summary>
    /// Whether an asset is available to serve. If <see langword="false"/>, the UI
    /// routes are never wired up.
    /// </summary>
    /// <remarks>
    /// UI assets are produced at build time. A package built in an environment
    /// without Node.js can end up empty; in that case, it is correct to never open
    /// the routes rather than serve a blank page — the consumer sees a 404 and
    /// looks for the reason.
    /// </remarks>
    bool HasAssets { get; }

    /// <summary>Serves a UI request.</summary>
    /// <param name="context">The request context.</param>
    /// <param name="basePath">
    /// The base path the UI is mounted under; always ends with <c>/</c>. Example:
    /// <c>/tracon/</c>. The implementation writes this into the
    /// <c>&lt;base href&gt;</c> tag inside <c>index.html</c>, so the UI works
    /// under any prefix.
    /// </param>
    /// <param name="relativePath">
    /// The requested asset path relative to the base path. An empty string for
    /// the root request. The leading <c>/</c> has been removed.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the request was served. If it returns
    /// <see langword="false"/>, the caller produces a <c>404</c>; in that case the
    /// implementation must have written <strong>nothing</strong> to the response.
    /// </returns>
    ValueTask<bool> TryServeAsync(HttpContext context, string basePath, string relativePath);
}
