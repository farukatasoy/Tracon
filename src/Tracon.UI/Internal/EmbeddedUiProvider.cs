using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Tracon;

/// <summary>
/// A UI provider that serves the single-page application embedded in the assembly.
/// </summary>
/// <remarks>
/// <para>
/// Text assets are Brotli-compressed and embedded at build time. If the client
/// accepts <c>br</c>, the content is sent as-is and no compression cost is
/// incurred at runtime. If not, the content is decompressed once and kept in
/// memory.
/// </para>
/// <para>
/// All state is held in the singleton instance, in immutable structures. Because
/// assets are produced at build time, there is no cache invalidation problem.
/// </para>
/// </remarks>
internal sealed class EmbeddedUiProvider : ITraconUiProvider
{
    private const string ImmutableCacheControl = "public,max-age=31536000,immutable";
    private const string RevalidateCacheControl = "no-cache";

    /// <summary>
    /// The Content Security Policy sent for the shell, up to the point where
    /// the inline scripts' hashes go.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The UI connects only to its own origin; it fetches no external script, font,
    /// or data. <c>style-src</c> includes <c>'unsafe-inline'</c> because React
    /// components' <c>style</c> attributes count as inline styles. <c>img-src</c>
    /// and <c>media-src</c> carry <c>blob:</c> because attachment preview and voice
    /// playback wrap bytes fetched via <c>fetch</c> with
    /// <c>URL.createObjectURL</c> - instead of a direct <c>&lt;img
    /// src="api/attachments/{id}"&gt;</c>/<c>&lt;audio src="..."&gt;</c>, which
    /// cannot carry a bearer token (<c>useAttachmentPreview</c>,
    /// <c>SpeakButton</c>) - so the source is always a <c>blob:</c> URL.
    /// <c>frame-ancestors 'none'</c> keeps the UI from being embedded in another
    /// page inside a frame - against clickjacking.
    /// </para>
    /// <para>
    /// <strong><c>script-src</c> stays free of <c>'unsafe-inline'</c>.</strong> The shell ships
    /// one inline script - the early theme paint - and it is allowed by the
    /// HASH of its own text, computed from the shipped shell in
    /// <see cref="BuildShell"/> rather than written down here. A literal hash
    /// would be a second copy of the script that nothing keeps in step: the
    /// script would go on being blocked the first time it changed, silently,
    /// because the page still works without it. A nonce was the other option
    /// and was rejected - it has to differ per response, which means rendering
    /// the shell per request and giving up the cached, ETagged document.
    /// </para>
    /// </remarks>
    private const string ContentSecurityPolicyBeforeHashes =
        "default-src 'none'; " +
        "script-src 'self'";

    /// <summary>The rest of the policy, after the inline scripts' hashes.</summary>
    /// <remarks>See <see cref="ContentSecurityPolicyBeforeHashes"/> for the whole rationale.</remarks>
    private const string ContentSecurityPolicyAfterHashes =
        "; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob:; " +
        "media-src 'self' blob:; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "base-uri 'self'; " +
        "form-action 'none'; " +
        "frame-ancestors 'none'";

    /// <summary>The placeholder written into the shell at build time and replaced at runtime.</summary>
    private const string BasePathPlaceholder = "__TRACON_BASE__";

    private readonly FrozenDictionary<string, UiAsset> _assets;
    private readonly Assembly _assembly;
    private readonly ConcurrentDictionary<string, byte[]> _stored = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte[]> _expanded = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ShellDocument> _shells = new(StringComparer.Ordinal);

    /// <summary>Creates a provider that reads the embedded assets.</summary>
    public EmbeddedUiProvider()
    {
        _assembly = typeof(EmbeddedUiProvider).Assembly;
        _assets = EmbeddedUiAssetCatalog.Load(_assembly);
    }

    /// <inheritdoc />
    public bool HasAssets => _assets.ContainsKey(EmbeddedUiAssetCatalog.ShellPath);

    /// <inheritdoc />
    public async ValueTask<bool> TryServeAsync(HttpContext context, string basePath, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrEmpty(basePath);
        ArgumentNullException.ThrowIfNull(relativePath);

        if (!HasAssets)
        {
            return false;
        }

        if (relativePath.Length > 0 && _assets.TryGetValue(relativePath, out var asset))
        {
            await WriteAssetAsync(context, asset).ConfigureAwait(false);

            return true;
        }

        // Not a known asset. This could be a deep UI route (e.g.,
        // runs/019fc0.../events). In a single-page app these paths do not exist
        // on the server; the shell is returned and the client handles routing.
        //
        // A path with a file extension is genuinely a missing asset. Returning
        // the shell would answer a missing script request with HTML; the browser
        // tries to parse it and the real error is hidden.
        if (HasFileExtension(relativePath))
        {
            return false;
        }

        await WriteShellAsync(context, basePath).ConfigureAwait(false);

        return true;
    }

    private async ValueTask WriteShellAsync(HttpContext context, string basePath)
    {
        var shell = _shells.GetOrAdd(basePath, BuildShell);

        context.Response.Headers.ContentSecurityPolicy = shell.ContentSecurityPolicy;
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "same-origin";

        await WriteBodyAsync(
            context,
            shell.Content,
            EmbeddedUiAssetCatalog.HtmlContentType,
            shell.ETag,
            RevalidateCacheControl,
            contentEncoding: null,
            varyOnEncoding: false).ConfigureAwait(false);
    }

    private async ValueTask WriteAssetAsync(HttpContext context, UiAsset asset)
    {
        if (string.Equals(asset.Path, EmbeddedUiAssetCatalog.ShellPath, StringComparison.Ordinal))
        {
            // The shell is always served with the base path already written in;
            // the raw version carries the placeholder and does not work in the
            // browser.
            await WriteShellAsync(context, ResolveBasePath(context)).ConfigureAwait(false);

            return;
        }

        var stored = ReadStored(asset);
        var acceptsBrotli = asset.IsBrotli && AcceptsBrotli(context.Request);
        var body = asset.IsBrotli && !acceptsBrotli ? Expand(asset, stored) : stored;

        // An ETag identifies one representation. The compressed and decompressed
        // forms of the same asset are different representations; if they carried
        // the same tag, an intermediate cache could serve the wrong encoding.
        var etag = acceptsBrotli ? ComputeETag(stored, "br") : ComputeETag(body, null);

        await WriteBodyAsync(
            context,
            body,
            asset.ContentType,
            etag,
            asset.Immutable ? ImmutableCacheControl : RevalidateCacheControl,
            acceptsBrotli ? "br" : null,
            varyOnEncoding: asset.IsBrotli).ConfigureAwait(false);
    }

    private static async ValueTask WriteBodyAsync(
        HttpContext context,
        byte[] body,
        string contentType,
        string etag,
        string cacheControl,
        string? contentEncoding,
        bool varyOnEncoding)
    {
        var response = context.Response;

        response.Headers.ETag = etag;
        response.Headers.CacheControl = cacheControl;

        if (varyOnEncoding)
        {
            response.Headers.Vary = "Accept-Encoding";
        }

        if (IsNotModified(context.Request, etag))
        {
            response.StatusCode = StatusCodes.Status304NotModified;

            return;
        }

        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = contentType;
        response.ContentLength = body.Length;

        if (contentEncoding is not null)
        {
            response.Headers.ContentEncoding = contentEncoding;
        }

        await response.Body.WriteAsync(body, context.RequestAborted).ConfigureAwait(false);
    }

    private ShellDocument BuildShell(string basePath)
    {
        var asset = _assets[EmbeddedUiAssetCatalog.ShellPath];
        var stored = ReadStored(asset);
        var raw = asset.IsBrotli ? Expand(asset, stored) : stored;

        var html = Encoding.UTF8.GetString(raw).Replace(
            BasePathPlaceholder,
            basePath,
            StringComparison.Ordinal);

        var content = Encoding.UTF8.GetBytes(html);

        return new ShellDocument(
            content,
            ComputeETag(content, null),
            BuildContentSecurityPolicy(html));
    }

    /// <summary>
    /// Builds the shell's policy, allowing each inline script by the hash of
    /// its own text.
    /// </summary>
    /// <param name="html">The shell as it will be sent, base path already written in.</param>
    /// <returns>The <c>Content-Security-Policy</c> header value.</returns>
    /// <remarks>
    /// The hash covers exactly the characters between the opening tag's
    /// <c>&gt;</c> and the closing <c>&lt;/script&gt;</c>, which is what a
    /// browser hashes - every byte of it, leading newline and indentation
    /// included. A script with a <c>src</c> is not inline and is skipped;
    /// <c>'self'</c> already covers it.
    /// </remarks>
    private static string BuildContentSecurityPolicy(string html)
    {
        var hashes = new StringBuilder();
        var cursor = 0;

        while (html.IndexOf("<script", cursor, StringComparison.OrdinalIgnoreCase) is var open && open >= 0)
        {
            var openEnd = html.IndexOf('>', open);
            var close = openEnd < 0 ? -1 : html.IndexOf("</script>", openEnd, StringComparison.OrdinalIgnoreCase);

            if (close < 0)
            {
                break;
            }

            var tag = html.AsSpan(open, openEnd - open);
            var body = html[(openEnd + 1)..close];

            if (!tag.Contains(" src=", StringComparison.OrdinalIgnoreCase) && body.Length > 0)
            {
                hashes.Append(" 'sha256-")
                      .Append(Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(body))))
                      .Append('\'');
            }

            cursor = close + "</script>".Length;
        }

        return ContentSecurityPolicyBeforeHashes + hashes + ContentSecurityPolicyAfterHashes;
    }

    private byte[] ReadStored(UiAsset asset)
        => _stored.GetOrAdd(asset.Path, _ =>
        {
            using var stream = _assembly.GetManifestResourceStream(asset.ResourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded UI asset '{asset.ResourceName}' could not be read.");

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);

            return buffer.ToArray();
        });

    private byte[] Expand(UiAsset asset, byte[] stored)
        => _expanded.GetOrAdd(asset.Path, _ =>
        {
            using var source = new MemoryStream(stored, writable: false);
            using var brotli = new BrotliStream(source, CompressionMode.Decompress);
            using var target = new MemoryStream();

            brotli.CopyTo(target);

            return target.ToArray();
        });

    /// <summary>
    /// Extracts the UI's base path from the request path.
    /// </summary>
    /// <remarks>
    /// If <c>index.html</c> is requested directly, the base path is the request's
    /// base path with the file name removed. The endpoint group does not use
    /// <c>PathBase</c>; the prefix lives in the path string itself.
    /// </remarks>
    private static string ResolveBasePath(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";
        var index = path.LastIndexOf('/');

        return index >= 0 ? path[..(index + 1)] : "/";
    }

    private static bool IsNotModified(HttpRequest request, string etag)
    {
        foreach (var value in request.Headers.IfNoneMatch)
        {
            if (value is null)
            {
                continue;
            }

            foreach (var candidate in value.Split(','))
            {
                if (string.Equals(candidate.Trim(), etag, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool AcceptsBrotli(HttpRequest request)
    {
        foreach (var value in request.Headers.AcceptEncoding)
        {
            if (value is null)
            {
                continue;
            }

            foreach (var candidate in value.Split(','))
            {
                var token = candidate.AsSpan().Trim();
                var separator = token.IndexOf(';');
                var quality = separator >= 0 ? token[(separator + 1)..].Trim() : default;

                if (separator >= 0)
                {
                    token = token[..separator].Trim();
                }

                if (!token.Equals("br", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // "br;q=0" means the encoding is explicitly rejected.
                return !quality.Equals("q=0", StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }

    private static string ComputeETag(byte[] content, string? encoding)
    {
        var hash = SHA256.HashData(content);
        var value = Convert.ToBase64String(hash, 0, 12).Replace('+', '-').Replace('/', '_');

        return encoding is null
            ? string.Create(CultureInfo.InvariantCulture, $"\"{value}\"")
            : string.Create(CultureInfo.InvariantCulture, $"\"{value}-{encoding}\"");
    }

    private static bool HasFileExtension(string path)
    {
        var lastSegment = path.LastIndexOf('/') is var slash && slash >= 0 ? path[(slash + 1)..] : path;

        return lastSegment.Contains('.', StringComparison.Ordinal);
    }

    private sealed record ShellDocument(byte[] Content, string ETag, string ContentSecurityPolicy);
}
