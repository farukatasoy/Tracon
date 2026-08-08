using Microsoft.AspNetCore.Http;

namespace AgentPrism;

/// <summary>
/// Bu istegi dogrulayan API anahtarini <see cref="HttpContext.Items"/> uzerinden
/// tasir.
/// </summary>
/// <remarks>
/// <see cref="AsyncLocal{T}"/> BILEREK kullanilmaz: bu tamamen istek kapsamli
/// bir degerdir ve <c>HttpContext.Items</c> zaten istek basina temizlenir.
/// <c>AsyncLocal</c> yazimi cagirana geri akmaz (docs/hafiza/cekirdek-calistirma.md)
/// — burada boyle bir sorun yoktur cunku deger, aynı `HttpContext` uzerinde
/// calisan sonraki katmanlarca okunur.
/// </remarks>
internal static class ApiKeyRequestContext
{
    private const string ItemsKey = "AgentPrism.ApiKeyRecord";

    /// <summary>Bu istegi dogrulayan anahtar kaydini saklar.</summary>
    /// <param name="httpContext">Gecerli istek.</param>
    /// <param name="record">Dogrulanan kayit.</param>
    public static void Set(HttpContext httpContext, ApiKeyRecord record)
        => httpContext.Items[ItemsKey] = record;

    /// <summary>Bu istegi dogrulayan anahtar kaydini okur.</summary>
    /// <param name="httpContext">Gecerli istek.</param>
    /// <returns>Kayit; istek bir API anahtariyla dogrulanmadiysa <see langword="null"/>.</returns>
    public static ApiKeyRecord? Get(HttpContext httpContext)
        => httpContext.Items.TryGetValue(ItemsKey, out var value) ? value as ApiKeyRecord : null;
}
