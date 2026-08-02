namespace AgentPrism;

/// <summary>
/// Istekten kiracinin nasil cozulecegini belirleyen ayarlar.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Varsayilan kapalidir.</strong> Tek kiracili kurulumda hicbir ek
/// yapilandirma gerekmez ve her istek
/// <see cref="AgentPrismOptions.DefaultTenantId"/> kiracisina duser.
/// </para>
/// <para>
/// 🚨 <strong>Baslik sahtelenebilir.</strong> Bir HTTP basligi kimlik kaniti
/// degildir; istemci istedigi degeri yazabilir. Bu yuzden:
/// </para>
/// <list type="bullet">
///   <item><description>
///   <see cref="ClaimType"/> ayarliysa ve istek kimlik dogrulamasindan gectiyse
///   <strong>yalnizca claim</strong> kullanilir; baslik yok sayilir.
///   </description></item>
///   <item><description>
///   Baslik yolu <see cref="AllowHeaderResolution"/> ile acikca acilmalidir ve
///   yalnizca guvenilen bir ag icinde (ya da yalnizca gelistirme sirasinda)
///   kullanilmalidir.
///   </description></item>
///   <item><description>
///   <see cref="AllowedTenants"/> doluysa cozulmeyen bir kiraci reddedilir;
///   listede olmayan bir deger varsayilan kiraciya <em>dusmez</em>.
///   </description></item>
/// </list>
/// <para>
/// Bu tip bilerek <c>record</c> degildir; ayar siniflarinin uretilmis
/// <c>ToString</c> metodu deger ifsa edebilir (karar K-035).
/// </para>
/// </remarks>
public sealed class AgentPrismTenancyOptions
{
    /// <summary>
    /// Cok kiracililik acik mi. Kapaliyken her istek varsayilan kiraciya duser.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Kiracinin okunacagi claim tipi. Ornek: <c>tenant_id</c>.
    /// Ayarliysa ve istek kimlik dogrulamasindan gectiyse basliktan once gelir.
    /// </summary>
    public string? ClaimType { get; set; }

    /// <summary>Kiracinin okunacagi HTTP basligi.</summary>
    public string HeaderName { get; set; } = "X-AgentPrism-Tenant";

    /// <summary>
    /// Kiraci basliktan cozulebilir mi. <strong>Varsayilan kapali</strong> —
    /// baslik sahtelenebilir.
    /// </summary>
    public bool AllowHeaderResolution { get; set; }

    /// <summary>
    /// Kabul edilen kiraci kimlikleri. Bos birakilirsa bicime uyan her deger kabul edilir.
    /// </summary>
    public IList<string> AllowedTenants { get; } = [];
}
