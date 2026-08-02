using System.Collections.Frozen;
using System.Reflection;

namespace AgentPrism;

/// <summary>
/// Assembly'ye gomulu arayuz varliklarinin dizini.
/// </summary>
/// <remarks>
/// <para>
/// Varliklar <c>AgentPrism.UI.wwwroot/</c> onekiyle gomulur. Onek kaldirildiginda
/// kalan ad, istemcinin gordugu yoldur.
/// </para>
/// <para>
/// <c>.br</c> ile biten bir kaynak Brotli ile sikistirilmis olarak saklanir ve
/// mantiksal yolu bu uzanti kaldirilmis halidir. Bir bildirim (manifest) dosyasina
/// gerek yoktur: saklama bicimi adin kendisinden okunur. Boylece derleme zinciri ile
/// calisma zamani arasinda senkron kalmasi gereken ikinci bir dosya olusmaz.
/// </para>
/// </remarks>
internal static class EmbeddedUiAssetCatalog
{
    /// <summary>Gomulu varliklarin kaynak adi oneki.</summary>
    public const string ResourcePrefix = "AgentPrism.UI.wwwroot/";

    /// <summary>Tek sayfa uygulamanin kabuk dosyasi.</summary>
    public const string ShellPath = "index.html";

    /// <summary>Kabugun icerik tipi.</summary>
    public const string HtmlContentType = "text/html; charset=utf-8";

    private static readonly FrozenDictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = HtmlContentType,
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".map"] = "application/json; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".ico"] = "image/x-icon",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".ttf"] = "font/ttf",
        [".txt"] = "text/plain; charset=utf-8",
        [".webmanifest"] = "application/manifest+json",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Assembly'deki arayuz varliklarini okur.</summary>
    /// <param name="assembly">Varliklarin gomulu oldugu assembly.</param>
    /// <returns>Yola gore dizinlenmis varliklar. Varlik yoksa bos sozluk.</returns>
    public static FrozenDictionary<string, UiAsset> Load(Assembly assembly)
    {
        var assets = new Dictionary<string, UiAsset>(StringComparer.Ordinal);

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            // MSBuild'in %(RecursiveDir) metadatasi Windows'ta ters bolu uretir.
            // Istemcinin gordugu yol her zaman ileri bolu tasir; normallestirme
            // burada yapilir, boylece paket hangi isletim sisteminde derlenirse
            // derlensin ayni yollari sunar.
            var name = resourceName[ResourcePrefix.Length..].Replace('\\', '/');
            var isBrotli = name.EndsWith(".br", StringComparison.Ordinal);
            var path = isBrotli ? name[..^3] : name;

            if (path.Length == 0)
            {
                continue;
            }

            assets[path] = new UiAsset
            {
                ResourceName = resourceName,
                Path = path,
                ContentType = ResolveContentType(path),
                IsBrotli = isBrotli,
                Immutable = path.StartsWith("assets/", StringComparison.Ordinal),
            };
        }

        return assets.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static string ResolveContentType(string path)
    {
        var lastDot = path.LastIndexOf('.');

        return lastDot >= 0 && ContentTypes.TryGetValue(path[lastDot..], out var contentType)
            ? contentType
            : "application/octet-stream";
    }
}
