using System.Text.RegularExpressions;

namespace AgentPrism;

/// <summary>
/// MCP tool adlarini AgentPrism defterine uygun hale getirir.
/// </summary>
/// <remarks>
/// <para>
/// Ad <c>{sunucu}_{tool}</c> bicimine getirilir. Onek zorunludur: iki farkli
/// sunucuda ayni adli tool bulunmasi olagandir ve onek olmadan biri digerini
/// gizlerdi.
/// </para>
/// <para>
/// <strong>Nokta kullanilmaz.</strong> OpenAI ve uyumlu saglayicilar fonksiyon
/// adlarinda yalnizca <c>[a-zA-Z0-9_-]</c> kabul eder; <c>sunucu.tool</c> bicimi
/// cagri aninda saglayici tarafindan reddedilirdi.
/// </para>
/// </remarks>
internal static partial class McpToolNaming
{
    /// <summary>Bir sunucu adinin tool adinda kullanilabilir olup olmadigini soyler.</summary>
    /// <param name="serverName">Sunucu adi.</param>
    /// <returns>Ad gecerliyse <see langword="true"/>.</returns>
    public static bool IsValidServerName(string? serverName)
        => !string.IsNullOrWhiteSpace(serverName) && SafeName().IsMatch(serverName);

    /// <summary>Sunucu ve tool adindan defterde kullanilacak adi uretir.</summary>
    /// <param name="serverName">Sunucu adi.</param>
    /// <param name="toolName">Uzak sunucudaki tool adi.</param>
    /// <returns>Onekli ad; tool adi gecersiz karakter tasiyorsa <see langword="null"/>.</returns>
    public static string? TryQualify(string serverName, string toolName)
        => SafeName().IsMatch(toolName) ? $"{serverName}_{toolName}" : null;

    [GeneratedRegex("^[a-zA-Z0-9_-]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SafeName();
}
