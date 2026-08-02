namespace AgentPrism;

/// <summary>
/// <c>MapAgentPrism</c> ile baglanan uclarin erisim ve davranis ayarlari.
/// </summary>
/// <remarks>
/// <para>
/// Erisim korumasi uc katmanlidir ve su sirayla uygulanir:
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// <strong>Loopback kisiti</strong> — <see cref="AllowRemoteAccess"/> kapaliyken
/// (varsayilan) loopback disindan gelen istek <c>403</c> alir. Kaza ile disariya
/// acilmaya karsi korumadir.
/// </description>
/// </item>
/// <item>
/// <description>
/// <strong>Bearer token</strong> — <see cref="AuthToken"/> doluysa
/// <c>Authorization: Bearer</c> basligi sabit zamanli karsilastirma ile denetlenir.
/// </description>
/// </item>
/// <item>
/// <description>
/// <strong>Authorization policy</strong> — <see cref="RequireAuthorization(string)"/>
/// ASP.NET Core kimlik dogrulama boru hattina baglanir. Uretimde kullanilan yol budur.
/// </description>
/// </item>
/// </list>
/// <para>
/// Bu tip bilerek <c>record</c> <strong>degildir</strong>. <c>record</c>'un derleyici
/// tarafindan uretilen <c>ToString</c> metodu tum ozellikleri yazar ve tek bir
/// gunluk satiri <see cref="AuthToken"/> degerini ifsa ederdi.
/// Gerekce: <c>docs/KARARLAR.md</c>, karar K-035.
/// </para>
/// </remarks>
public sealed class AgentPrismEndpointOptions
{
    /// <summary>
    /// Loopback disindan gelen isteklere izin verilir mi. Varsayilan
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Ters vekil uyarisi.</strong> Uygulama bir ters vekil arkasindaysa
    /// baglantinin uzak adresi vekilin adresidir ve bu adres genellikle loopback'tir.
    /// Bu durumda loopback kisiti hicbir sey korumaz. Ters vekil arkasinda
    /// <c>ForwardedHeaders</c> ara yazilimini yapilandirin ve korumayi
    /// <see cref="AuthToken"/> veya <see cref="RequireAuthorization(string)"/>
    /// uzerine kurun.
    /// </para>
    /// </remarks>
    public bool AllowRemoteAccess { get; set; }

    /// <summary>
    /// Beklenen bearer token. Bos birakilirsa token denetimi yapilmaz.
    /// </summary>
    /// <remarks>
    /// Bu bir <strong>sirdir</strong>. Yapilandirma dosyasina yazmayin;
    /// <c>dotnet user-secrets</c> veya ortam degiskeni kullanin. Deger hicbir
    /// yanitta, <c>/api/meta</c> ciktisinda veya gunluk satirinda gorunmez.
    /// </remarks>
    public string? AuthToken { get; set; }

    /// <summary>
    /// Uclara uygulanacak ASP.NET Core authorization policy adi.
    /// <see cref="RequireAuthorization(string)"/> ile ayarlanir.
    /// </summary>
    public string? AuthorizationPolicy { get; private set; }

    /// <summary>
    /// Calisan bir calistirmanin olaylari akitilirken iki yoklama arasindaki sure.
    /// Varsayilan 250 ms.
    /// </summary>
    /// <remarks>
    /// Olay deposu bir bildirim kanali sunmaz; canli akis deponun yeni olaylar icin
    /// yoklanmasiyla saglanir. Kucuk deger gecikmeyi dusurur, veritabani yukunu artirir.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Deger pozitif degilse.</exception>
    public TimeSpan RunEventPollInterval
    {
        get;

        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Uclari bir ASP.NET Core authorization policy'sine baglar.
    /// </summary>
    /// <param name="policyName">Uygulanacak policy adi.</param>
    /// <exception cref="ArgumentException"><paramref name="policyName"/> bos ise.</exception>
    /// <remarks>
    /// Policy, <c>{prefix}/api/meta</c> disindaki tum uclara uygulanir. Meta ucu
    /// arayuzun hangi kimlik yontemini kullanacagini ogrenmesi icin acik kalir ve
    /// hicbir hassas veri dondurmez.
    /// </remarks>
    public void RequireAuthorization(string policyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
        AuthorizationPolicy = policyName;
    }
}
