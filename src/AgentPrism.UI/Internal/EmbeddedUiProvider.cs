using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace AgentPrism;

/// <summary>
/// Assembly'ye gomulu tek sayfa uygulamayi sunan arayuz kaynagi.
/// </summary>
/// <remarks>
/// <para>
/// Metin varliklar derleme sirasinda Brotli ile sikistirilip gomulur. Istemci
/// <c>br</c> kabul ediyorsa icerik oldugu gibi gonderilir ve calisma aninda hicbir
/// sikistirma maliyeti olusmaz. Kabul etmiyorsa icerik bir kez acilir ve bellekte
/// tutulur.
/// </para>
/// <para>
/// Tum durum, tekil ornekte ve degismez yapilar icinde tutulur. Varliklar derleme
/// aninda uretildigi icin gecersiz kilma (invalidation) sorunu yoktur.
/// </para>
/// </remarks>
internal sealed class EmbeddedUiProvider : IAgentPrismUiProvider
{
    private const string ImmutableCacheControl = "public,max-age=31536000,immutable";
    private const string RevalidateCacheControl = "no-cache";

    /// <summary>
    /// Kabuk icin gonderilen icerik guvenlik politikasi.
    /// </summary>
    /// <remarks>
    /// Arayuz yalnizca kendi kaynagina baglanir; disaridan betik, yazi tipi veya
    /// veri cekmez. <c>style-src</c> icinde <c>'unsafe-inline'</c> vardir cunku
    /// React bilesenlerinin <c>style</c> nitelikleri satir ici stil sayilir.
    /// <c>img-src</c> ve <c>media-src</c> <c>blob:</c> taşır: ek onizlemesi ve
    /// seslendirme oynatimi, bearer token tasiyamayan dogrudan <c>&lt;img
    /// src="api/attachments/{id}"&gt;</c>/<c>&lt;audio src="..."&gt;</c> yerine
    /// <c>fetch</c> ile cekilen baytlari <c>URL.createObjectURL</c> ile sarar
    /// (<c>useAttachmentPreview</c>, <c>SpeakButton</c>) - kaynak her zaman bir
    /// <c>blob:</c> URL'idir. <c>frame-ancestors 'none'</c> arayuzun baska bir
    /// sayfaya cerceve icinde gomulmesini engeller - tiklama hirsizligina
    /// (clickjacking) karsi.
    /// </remarks>
    private const string ContentSecurityPolicy =
        "default-src 'none'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob:; " +
        "media-src 'self' blob:; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "base-uri 'self'; " +
        "form-action 'none'; " +
        "frame-ancestors 'none'";

    /// <summary>Derleme sirasinda kabuga yazilan, calisma aninda degistirilen yer tutucu.</summary>
    private const string BasePathPlaceholder = "__AGENTPRISM_BASE__";

    private readonly FrozenDictionary<string, UiAsset> _assets;
    private readonly Assembly _assembly;
    private readonly ConcurrentDictionary<string, byte[]> _stored = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte[]> _expanded = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ShellDocument> _shells = new(StringComparer.Ordinal);

    /// <summary>Gomulu varliklari okuyan bir kaynak olusturur.</summary>
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

        // Bilinen bir varlik degil. Derinlemesine bir arayuz rotasi olabilir
        // (ornek: runs/019fc0.../events). Tek sayfa uygulamada bu yollar sunucuda
        // yoktur; kabuk dondurulur ve yonlendirmeyi istemci yapar.
        //
        // Uzantisi olan bir yol ise gercekten eksik bir varliktir. Kabugu dondurmek
        // eksik bir betik istegine HTML ile yanit vermek olurdu; tarayici bunu
        // cozumlemeye calisir ve hata mesaji nedeni gizler.
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

        context.Response.Headers.ContentSecurityPolicy = ContentSecurityPolicy;
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
            // Kabuk her zaman taban yolu yazilmis haliyle sunulur; ham hali
            // yer tutucu tasir ve tarayicida calismaz.
            await WriteShellAsync(context, ResolveBasePath(context)).ConfigureAwait(false);

            return;
        }

        var stored = ReadStored(asset);
        var acceptsBrotli = asset.IsBrotli && AcceptsBrotli(context.Request);
        var body = asset.IsBrotli && !acceptsBrotli ? Expand(asset, stored) : stored;

        // ETag bir temsili tanimlar. Ayni varligin sikistirilmis ve acilmis halleri
        // farkli temsillerdir; ayni etiketi tasirlarsa bir ara onbellek yanlis
        // kodlamayi servis edebilir.
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

        return new ShellDocument(content, ComputeETag(content, null));
    }

    private byte[] ReadStored(UiAsset asset)
        => _stored.GetOrAdd(asset.Path, _ =>
        {
            using var stream = _assembly.GetManifestResourceStream(asset.ResourceName)
                ?? throw new InvalidOperationException(
                    $"Gomulu arayuz varligi '{asset.ResourceName}' okunamadi.");

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
    /// Istegin yolundan arayuzun taban yolunu cikarir.
    /// </summary>
    /// <remarks>
    /// <c>index.html</c> dogrudan istenmisse taban yol, istegin taban yolu ile
    /// dosya adinin cikarilmis halidir. Uc grubu <c>PathBase</c> kullanmaz;
    /// onek yol dizesinin kendisindedir.
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

                // "br;q=0" kodlamanin acikca reddedildigi anlamina gelir.
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

    private sealed record ShellDocument(byte[] Content, string ETag);
}
