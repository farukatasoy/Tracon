namespace AgentPrism;

/// <summary>
/// Bir eki <see cref="Microsoft.Extensions.AI.UriContent"/> referansina cevirir
/// ve bu referansi geri cozer.
/// </summary>
/// <remarks>
/// Tanima yalnizca <c>/api/attachments/{id}</c> izine bakar; yol onekini
/// (<c>{prefix}</c>) bilmeye ihtiyac duymaz ve goreli veya mutlak her iki Uri
/// bicimini de tanir. Bu sayede <c>AgentPrism.Core</c>, uc noktalarin
/// yapilandirmasindan (onek, host) tamamen bagimsiz kalir.
/// Gerekce: <c>docs/14-COK-MODLULUK.md</c>, bolum 14.1.
/// </remarks>
public static class AttachmentUriReference
{
    private const string Marker = "/api/attachments/";

    /// <summary>Bir ek kimligini referans Uri'sine cevirir.</summary>
    /// <param name="prefix">AgentPrism uc noktalarinin yol oneki (ornek: <c>/agentprism</c>).</param>
    /// <param name="attachmentId">Ek kimligi.</param>
    /// <returns>Goreli bir Uri.</returns>
    public static Uri Create(string prefix, Guid attachmentId)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        return new Uri($"{prefix.TrimEnd('/')}{Marker}{attachmentId}", UriKind.Relative);
    }

    /// <summary>Bir Uri'nin ek referansi olup olmadigini denetler ve kimligi cikarir.</summary>
    /// <param name="uri">Denetlenecek Uri.</param>
    /// <param name="attachmentId">Bulunursa ek kimligi.</param>
    /// <returns>Uri bir ek referansiysa <see langword="true"/>.</returns>
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
