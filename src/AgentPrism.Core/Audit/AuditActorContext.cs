using System.Security.Claims;

namespace AgentPrism;

/// <summary>
/// Gecerli caginin kullanicisini tasiyan ortam (ambient) baglam.
/// </summary>
/// <remarks>
/// <para>
/// <c>AgentPrism.AspNetCore</c>, her korumali istegin basinda (guvenlik denetimleri
/// gectikten sonra) <see cref="Current"/> alanina <c>HttpContext.User</c>'i yazar.
/// Deger bir <see cref="AsyncLocal{T}"/> icinde tutuldugu icin ayni istegin async
/// cagri zincirinde ilerideki her noktadan (ornegin bir depo dekoratorunden)
/// okunabilir — <c>Activity.Current</c> ile ayni mekanizma.
/// </para>
/// <para>
/// Bu tasarim, <c>AgentPrism.Core</c>'un <c>IHttpContextAccessor</c> veya ASP.NET
/// Core'a hicbir bagimlilik eklemeden aktoru okuyabilmesini saglar:
/// <see cref="ClaimsPrincipal"/> temel .NET kutuphanesindedir, ASP.NET Core'a
/// ozgu degildir.
/// </para>
/// </remarks>
public static class AuditActorContext
{
    private static readonly AsyncLocal<ClaimsPrincipal?> CurrentHolder = new();

    /// <summary>Gecerli caginin kullanicisi. Ayarlanmamissa <see langword="null"/>.</summary>
    public static ClaimsPrincipal? Current
    {
        get => CurrentHolder.Value;
        set => CurrentHolder.Value = value;
    }
}
