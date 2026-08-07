namespace AgentPrism;

/// <summary>
/// Modele giden ve modelden gelen icerigi denetleyen genisleme noktasi.
/// </summary>
/// <remarks>
/// <para>
/// Guard <strong>opt-in</strong>'dir: <c>AddAgentPrism()</c> tek basina hicbir
/// <see cref="IContentGuard"/> kaydetmez. Hic guard kayitli degilse denetim
/// sarmalayicisi model boru hattina <strong>eklenmez</strong> ve maliyet tam
/// olarak sifirdir — bir bayrak denetimi bile calismaz.
/// </para>
/// <para>
/// Kayit <c>TryAddEnumerable</c> ile yapilir; birden cok guard sirayla calisir ve
/// <strong>en sert karar kazanir</strong> (<see cref="ContentGuardAction.Block"/> &gt;
/// <see cref="ContentGuardAction.Mask"/> &gt; <see cref="ContentGuardAction.Allow"/>).
/// Kayit sirasina bagli bir "ilk karar kazanir" kurali secilmedi: <c>TryAddEnumerable</c>
/// sirasi garanti edilmez ve guvenlik karari sirayla degismemelidir.
/// </para>
/// <para>
/// 🚨 Guard model boru hattinin <strong>EN DISINDA</strong> calisir: engellenen bir
/// istek aga hic cikmaz (para harcanmaz) ve engelleme devre kesiciyi tetiklemez
/// (arka arkaya engellenen istekler saglayiciyi kapatmaz).
/// </para>
/// <para>
/// 🚨 Guard bir <em>gozlem araci degil, bir kontroldur.</em> "Gozlemlenebilirlik
/// islevselligi bozmaz" kurali burada gecerli DEGILDIR: bu metot istisna
/// atarsa calistirma basarisiz olur. Denetlenemeyen icerik gecirilmez.
/// </para>
/// <para>
/// Uygulama <strong>sicak yoldadir</strong> ve her model cagrisinda calisir.
/// Eslesme yokken yeni bir dize tahsis etmemesi beklenir.
/// </para>
/// </remarks>
public interface IContentGuard
{
    /// <summary>
    /// Guard'in adi. Denetim izine ve calistirma olayina bu ad yazilir.
    /// </summary>
    string Name { get; }

    /// <summary>Icerigi denetler.</summary>
    /// <param name="context">Denetim baglami.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>
    /// Karar. Icerik degismeden gececekse <see cref="ContentGuardResult.Allow"/>
    /// dondurulur; bu yol tahsis uretmez.
    /// </returns>
    ValueTask<ContentGuardResult> InspectAsync(
        ContentGuardContext context,
        CancellationToken cancellationToken = default);
}
