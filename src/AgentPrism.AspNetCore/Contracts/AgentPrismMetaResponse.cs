namespace AgentPrism;

/// <summary>
/// <c>{prefix}/api/meta</c> yaniti. Arayuzun kendini yapilandirmasi icin gereken
/// en az bilgiyi tasir.
/// </summary>
/// <remarks>
/// Bu uc <strong>kimlik dogrulamasi olmadan</strong> erisilebilir; arayuz hangi
/// kimlik yontemini kullanacagini baska turlu ogrenemez. Bu yuzden icerigi
/// bilerek dardir: sir, kiraci verisi, agent adi veya sayim bilgisi icermez.
/// Gerekce: <c>docs/KARARLAR.md</c>, karar K-010.
/// </remarks>
public sealed record AgentPrismMetaResponse
{
    /// <summary>AgentPrism surumu.</summary>
    public required string Version { get; init; }

    /// <summary>Uclarin baglandigi yol oneki. Arayuz kendi cagrilarini buna gore kurar.</summary>
    public required string Prefix { get; init; }

    /// <summary>Aktif kimlik dogrulama yontemleri.</summary>
    public required AgentPrismAuthenticationMeta Authentication { get; init; }

    /// <summary>Aktif depolama uygulamalari.</summary>
    public required AgentPrismStorageMeta Storage { get; init; }

    /// <summary>Gecerli caginin rol yetkileri.</summary>
    public required AgentPrismRoleMeta Roles { get; init; }
}

/// <summary>
/// Gecerli isteğin sahibinin hangi rol seviyelerini karsiladigini bildirir.
/// </summary>
/// <remarks>
/// Arayuz yetkisi olmayan duzenleme dugmelerini bu alana gore gizler; sunucu
/// tarafi yetkilendirme yine de tek gercektir, bu alan bir guvenlik onlemi
/// <strong>degildir</strong>. Ilgili rol policy'si (<see cref="AgentPrismPolicies"/>)
/// tuketicinin authorization yapilandirmasinda kayitli degilse karsilik gelen
/// alan <see langword="true"/> doner: rol kisiti yoktur, uc yalnizca mevcut
/// uc katmanli korumadan gecer.
/// </remarks>
public sealed record AgentPrismRoleMeta
{
    /// <summary>Agent, calistirma, oturum, trace ve istatistik okuma yetkisi var mi.</summary>
    public required bool CanRead { get; init; }

    /// <summary>Reader'a ek olarak calistirma baslatma, onay verme, oturum silme yetkisi var mi.</summary>
    public required bool CanOperate { get; init; }

    /// <summary>Agent tanimi yazma, MCP sunucusu ekleme, kiraci ve denetim izi yonetimi yetkisi var mi.</summary>
    public required bool CanAdminister { get; init; }
}

/// <summary>
/// Hangi kimlik dogrulama katmanlarinin acik oldugunu bildirir.
/// </summary>
/// <remarks>
/// Yalnizca <see langword="bool"/> alanlar tasir. Policy adi bilerek
/// <strong>dondurulmez</strong>: arayuz o adla bir sey yapamaz ve ad, kimlik
/// dogrulamasi olmayan bir uctan sizan bir yapilandirma ayrintisi olurdu.
/// </remarks>
public sealed record AgentPrismAuthenticationMeta
{
    /// <summary>Loopback disindan erisime izin veriliyor mu.</summary>
    public required bool AllowRemoteAccess { get; init; }

    /// <summary><c>Authorization: Bearer</c> basligi bekleniyor mu.</summary>
    public required bool RequiresBearerToken { get; init; }

    /// <summary>Bir ASP.NET Core authorization policy uygulaniyor mu.</summary>
    public required bool RequiresAuthorizationPolicy { get; init; }
}

/// <summary>
/// Hangi depolama uygulamalarinin aktif oldugunu bildirir.
/// </summary>
/// <remarks>
/// Bellek ici depolar desteklenen bir moddur, bir test yardimcisi degildir
/// (karar K-018). Ancak sinirlari vardir — surec omru ve tek dugum — ve arayuz
/// bunu kullaniciya gosterebilmelidir.
/// </remarks>
public sealed record AgentPrismStorageMeta
{
    /// <summary>
    /// Uc deponun ucu de kalici mi. Herhangi biri bellek ici ise
    /// <see langword="false"/> doner.
    /// </summary>
    public required bool Persistent { get; init; }

    /// <summary>Agent tanimi deposunun tip adi.</summary>
    public required string AgentDefinitionStore { get; init; }

    /// <summary>Calistirma deposunun tip adi.</summary>
    public required string RunStore { get; init; }

    /// <summary>Oturum deposunun tip adi.</summary>
    public required string SessionStore { get; init; }
}
