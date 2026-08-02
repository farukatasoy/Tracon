using Microsoft.AspNetCore.Http;

namespace AgentPrism;

/// <summary>
/// Yonetim arayuzunun statik varliklarini sunan kaynak.
/// </summary>
/// <remarks>
/// <para>
/// Bu soyutlama bilerek <strong>tek metotludur</strong>. Varlik listesi, icerik tipi,
/// <c>ETag</c>, onbellek basliklari, sikistirma bicimi ve tek sayfa uygulama geri
/// donusu tamamen uygulamaya aittir. Boylece arayuz paketi kendi paketleme bicimini
/// degistirdiginde HTTP katmaninin public API'si degismez.
/// </para>
/// <para>
/// Bagimlilik yonu <c>AgentPrism.UI → AgentPrism.AspNetCore</c> seklindedir ve
/// tersine cevrilemez. Bu yuzden <c>MapAgentPrism</c> arayuz paketini dogrudan
/// cagiramaz; kaydi servis saglayicidan cozer. Kayit yoksa arayuz rotalari hic
/// baglanmaz ve HTTP yuzeyi Faz 4'teki haliyle kalir.
/// </para>
/// <para>
/// Uygulamalar <c>TryAdd</c> ile kaydedilir; tuketicinin kendi kaydi kazanir (kural K4).
/// </para>
/// </remarks>
public interface IAgentPrismUiProvider
{
    /// <summary>
    /// Sunulacak varlik var mi. <see langword="false"/> ise arayuz rotalari
    /// hic baglanmaz.
    /// </summary>
    /// <remarks>
    /// Arayuz varliklari derleme sirasinda uretilir. Node.js bulunmayan bir ortamda
    /// derlenen bir paket bos kalabilir; bu durumda bos bir sayfa sunmak yerine
    /// rotalari hic acmamak dogrudur - tüketici 404 gorur ve nedenini arar.
    /// </remarks>
    bool HasAssets { get; }

    /// <summary>Bir arayuz istegini karsilar.</summary>
    /// <param name="context">Istek baglami.</param>
    /// <param name="basePath">
    /// Arayuzun baglandigi taban yol; her zaman <c>/</c> ile biter. Ornek:
    /// <c>/agentprism/</c>. Uygulama bunu <c>index.html</c> icindeki
    /// <c>&lt;base href&gt;</c> etiketine yazar; boylece arayuz herhangi bir
    /// onek altinda calisir.
    /// </param>
    /// <param name="relativePath">
    /// Taban yola gore istenen varlik yolu. Kok istegi icin bos dizedir.
    /// Bas taraftaki <c>/</c> kaldirilmistir.
    /// </param>
    /// <returns>
    /// Istek karsilandiysa <see langword="true"/>. <see langword="false"/> donerse
    /// cagiran <c>404</c> uretir; uygulama bu durumda yanita <strong>hicbir sey
    /// yazmamis</strong> olmalidir.
    /// </returns>
    ValueTask<bool> TryServeAsync(HttpContext context, string basePath, string relativePath);
}
