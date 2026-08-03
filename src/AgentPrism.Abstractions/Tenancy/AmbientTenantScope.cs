namespace AgentPrism;

/// <summary>
/// Arka planda (bir HTTP istegi disinda) yurutulen bir islemi belirli bir
/// kiraci olarak calistirmak icin ortam (ambient) gecis mekanizmasi.
/// </summary>
/// <remarks>
/// <see cref="ITenantContext"/> uygulamalari (tek kiracili varsayilan,
/// HTTP tabanli cok kiracili cozumleyici) teklidir (singleton) ve kiraciyi ya
/// sabit bir varsayilandan ya da <c>HttpContext</c>'ten okur. Zamanlanmis bir
/// isin hangi kiraci icin calistigi ise ne sabittir ne de HTTP baglaminda
/// yasar — <c>JobRecord.TenantId</c> icinde tasinir. Bu sinif,
/// <c>IHttpContextAccessor</c>'in kullandigi ayni <see cref="AsyncLocal{T}"/>
/// deseniyle bir gecis saglar: deger ayarliyken her iki
/// <see cref="ITenantContext"/> uygulamasi da kendi varsayilan cozumlemesinden
/// once bunu kontrol eder.
/// </remarks>
public static class AmbientTenantScope
{
    private static readonly AsyncLocal<string?> Ambient = new();

    /// <summary>Su an ayarliysa gecerli ortam kiracisi; degilse <see langword="null"/>.</summary>
    public static string? Current => Ambient.Value;

    /// <summary>
    /// Kapsam suresince ortam kiracisini ayarlar.
    /// </summary>
    /// <param name="tenantId">Kapsam suresince kullanilacak kiraci kimligi.</param>
    /// <returns>
    /// <see cref="IDisposable.Dispose"/> cagrildiginda onceki degeri geri
    /// yukleyen bir nesne. Ic ice kullanim guvenlidir.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="tenantId"/> bos veya bosluktan ibaretse.</exception>
    public static IDisposable Begin(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var previous = Ambient.Value;
        Ambient.Value = tenantId;

        return new RestoreScope(previous);
    }

    private sealed class RestoreScope(string? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}
