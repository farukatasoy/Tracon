namespace AgentPrism;

/// <summary>Bir guard'in verebilecegi karar.</summary>
/// <remarks>
/// Siralama <strong>anlamlidir</strong>: birden cok guard calistiginda en buyuk
/// deger kazanir. Yeni bir karar eklenirse sertlik sirasina gore yerlestirilmelidir.
/// </remarks>
public enum ContentGuardAction
{
    /// <summary>Icerik degismeden gecer.</summary>
    Allow = 0,

    /// <summary>Icerik degistirilerek gecer. Calistirma devam eder.</summary>
    Mask = 1,

    /// <summary>Icerik engellenir. Calistirma <c>Failed</c> olur.</summary>
    Block = 2,
}

/// <summary>Bir <see cref="IContentGuard"/> denetiminin sonucu.</summary>
/// <remarks>
/// 🚨 Sonuc <strong>engellenen icerigi tasimaz</strong>. Yalnizca eslesen kuralin
/// adi ve engelleme sebebi tasinir; ikisi de denetim izine ve calistirma olayina
/// yazilir. Engellenen icerik tanimi geregi hassastir ve onu bir izin icine yazmak
/// sorunu <em>kalici</em> hale getirir (K-059'un ruhu).
/// </remarks>
public sealed record ContentGuardResult
{
    /// <summary>Icerik degismeden gecer.</summary>
    /// <remarks>
    /// Tek bir ornek paylasilir: bu, guard'larin en sik dondurdugu degerdir ve
    /// sicak yolda tahsis uretmemelidir.
    /// </remarks>
    public static ContentGuardResult Allow { get; } = new() { Action = ContentGuardAction.Allow };

    /// <summary>Verilen karar.</summary>
    public required ContentGuardAction Action { get; init; }

    /// <summary>
    /// Modele gonderilecek (veya istemciye donecek) yeni metin. Yalnizca
    /// <see cref="ContentGuardAction.Mask"/> icin dolar.
    /// </summary>
    public string? MaskedText { get; init; }

    /// <summary>Eslesen kuralin adi. 🚨 Eslesen ICERIGI tasimaz.</summary>
    public string? RuleName { get; init; }

    /// <summary>Engelleme sebebi. 🚨 Engellenen METNI tasimaz.</summary>
    public string? Reason { get; init; }

    /// <summary>Icerik degistirilerek gecer.</summary>
    /// <param name="maskedText">Iceriğin yeni hali.</param>
    /// <param name="ruleName">Eslesen kuralin adi.</param>
    /// <returns>Maskeleme karari.</returns>
    public static ContentGuardResult Mask(string maskedText, string ruleName) => new()
    {
        Action = ContentGuardAction.Mask,
        MaskedText = maskedText,
        RuleName = ruleName,
    };

    /// <summary>Icerik engellenir; calistirma <c>Failed</c> olur.</summary>
    /// <param name="ruleName">Eslesen kuralin adi.</param>
    /// <param name="reason">Engelleme sebebi. Engellenen metni TASIMAMALIDIR.</param>
    /// <returns>Engelleme karari.</returns>
    public static ContentGuardResult Block(string ruleName, string reason) => new()
    {
        Action = ContentGuardAction.Block,
        RuleName = ruleName,
        Reason = reason,
    };
}
