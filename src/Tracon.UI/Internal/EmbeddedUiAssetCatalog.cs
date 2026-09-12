using System.Collections.Frozen;
using System.Reflection;

namespace Tracon;

/// <summary>
/// Index of the UI assets embedded in the assembly.
/// </summary>
/// <remarks>
/// <para>
/// Assets are embedded with the <c>Tracon.UI.wwwroot/</c> prefix. Once the prefix is
/// removed, the remaining name is the path the client sees.
/// </para>
/// <para>
/// A resource ending in <c>.br</c> is stored Brotli-compressed, and its logical path is
/// the name with that extension removed. No manifest file is needed: the storage format
/// is read from the name itself. This avoids a second file that would have to stay in
/// sync between the build chain and the runtime.
/// </para>
/// </remarks>
internal static class EmbeddedUiAssetCatalog
{
    /// <summary>The resource name prefix for embedded assets.</summary>
    public const string ResourcePrefix = "Tracon.UI.wwwroot/";

    /// <summary>The shell file of the single-page application.</summary>
    public const string ShellPath = "index.html";

    /// <summary>The content type of the shell.</summary>
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

    /// <summary>Reads the UI assets in the assembly.</summary>
    /// <param name="assembly">The assembly the assets are embedded in.</param>
    /// <returns>Assets indexed by path. An empty dictionary if there are no assets.</returns>
    public static FrozenDictionary<string, UiAsset> Load(Assembly assembly)
    {
        var assets = new Dictionary<string, UiAsset>(StringComparer.Ordinal);

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            // MSBuild's %(RecursiveDir) metadata produces backslashes on Windows.
            // The path the client sees always uses forward slashes; normalization
            // happens here, so the package serves the same paths regardless of
            // which operating system it was built on.
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
