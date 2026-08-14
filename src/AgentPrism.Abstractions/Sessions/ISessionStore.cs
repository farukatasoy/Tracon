using System.Text.Json;

namespace AgentPrism;

/// <summary>Serilestirilmis agent oturumlarinin deposu.</summary>
/// <remarks>
/// Oturum durumu, Microsoft Agent Framework'un <c>AgentSession</c> nesnesinin
/// serilestirilmis halidir ve <strong>opak</strong> kabul edilir. Icerigi
/// yorumlanmaz; yalnizca saklanir ve geri yuklenir.
/// </remarks>
public interface ISessionStore
{
    /// <summary>Oturumu kaydeder. Ayni kimlikle kayit varsa uzerine yazar.</summary>
    /// <param name="record">Kaydedilecek oturum.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    ValueTask SaveAsync(SessionRecord record, CancellationToken cancellationToken = default);

    /// <summary>Yeni bir oturum kaydini yalnizca YOKSA olusturur.</summary>
    /// <remarks>
    /// <para>
    /// Ayni <see cref="SessionRecord.Id"/> ile eszamanli iki cagri geldiginde
    /// YALNIZ biri <see langword="true"/> donmelidir; kaybeden <see langword="false"/>
    /// alip <see cref="GetAsync"/> ile kazananin kaydini okumalidir.
    /// </para>
    /// <para>
    /// 🚨 Varsayilan uygulama ATOMIK DEGILDIR (check-then-create) — yalnizca
    /// bu yontemi henuz gecersiz kilmamis eski depolarin derlenmeye devam
    /// etmesi icindir. Gercek depolar (<c>SqlSessionStore</c>, <c>InMemorySessionStore</c>)
    /// bu yontemi GERCEKTEN atomik olarak gecersiz kilar. HATA-004: atomiklik
    /// olmadan, ayni YENI oturuma gelen eszamanli iki ilk istek birbirinden
    /// habersiz iki farkli konusma kimligi uretir; ikinci <see cref="SaveAsync"/>
    /// birinciyi kosulsuzca ezer ve kaybedenin mesajlari sessizce erisilmez kalir.
    /// </para>
    /// </remarks>
    /// <param name="record">Olusturulacak oturum.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Olusturulduysa <see langword="true"/>; ayni kimlikle kayit zaten varsa <see langword="false"/>.</returns>
    async ValueTask<bool> TryCreateAsync(SessionRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (await GetAsync(record.Id, cancellationToken).ConfigureAwait(false) is not null)
        {
            return false;
        }

        await SaveAsync(record, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Oturumu getirir.</summary>
    /// <param name="sessionId">Oturum kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Oturum; yoksa <see langword="null"/>.</returns>
    ValueTask<SessionRecord?> GetAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir oturum kimliginin sahibi olan kiraciyi, <see cref="ITenantContext"/>'ten
    /// okunan ambient kiraci filtresine BAKMAKSIZIN dondurur.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🚨 <see cref="GetAsync"/> ambient kiraciyle filtrelenir; bu yuzden "bu kimlik
    /// BASKA bir kiraciya mi ait" sorusunu asla cevaplayamaz — cagiran zaten kendi
    /// kiracisinin baglamindadir ve baska kiracinin kaydi o baglamdan hicbir zaman
    /// GORULMEZ, sonuc daima <see langword="null"/> olur. HATA-S2-005: OpenAI uyumlu
    /// uclarin capraz kiraci sahiplik denetimi tam olarak bu yuzden olu koddu — reddetme
    /// dali hicbir zaman tetiklenmiyordu, kimlik sessizce "hic kullanilmamis" sayilip
    /// yeni bir oturum aciliyordu.
    /// </para>
    /// <para>
    /// 🚨 Varsayilan uygulama <see cref="GetAsync"/>'i cagirir — dolayisiyla YUKARIDAKI
    /// KUSURU TASIR ve capraz kiraci sorusuna asla dogru cevap veremez. Bu, yalnizca
    /// bu yontemi henuz gecersiz kilmamis eski/ozel depolarin derlenmeye devam etmesi
    /// icindir. Gercek depolar (<c>SqlSessionStore</c>, <c>InMemorySessionStore</c>)
    /// bu yontemi kiraciden BAGIMSIZ, gercekten dogru bir uygulamayla gecersiz kilar.
    /// </para>
    /// </remarks>
    /// <param name="sessionId">Oturum kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>
    /// Kimlik kullanilmissa sahibi kiracinin kimligi; kimlik hic kullanilmamissa
    /// <see langword="null"/>.
    /// </returns>
    async ValueTask<string?> GetOwnerTenantIdAsync(string sessionId, CancellationToken cancellationToken = default)
        => (await GetAsync(sessionId, cancellationToken).ConfigureAwait(false))?.TenantId;

    /// <summary>Oturumu siler.</summary>
    /// <param name="sessionId">Oturum kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Silme gerceklestiyse <see langword="true"/>.</returns>
    ValueTask<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Oturumlari filtreleyerek listeler. En son guncellenen basta doner.</summary>
    /// <param name="query">Filtre.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Oturumlar.</returns>
    ValueTask<IReadOnlyList<SessionRecord>> QueryAsync(SessionQuery query, CancellationToken cancellationToken = default);
}

/// <summary>Saklanan bir oturum.</summary>
public sealed record SessionRecord
{
    /// <summary>Oturum kimligi.</summary>
    public required string Id { get; init; }

    /// <summary>Oturumun ait oldugu agent.</summary>
    public required string AgentName { get; init; }

    /// <summary>
    /// Serilestirilmis oturum durumu. Microsoft Agent Framework'un
    /// <c>SerializeSessionAsync</c> ciktisidir ve opak kabul edilir.
    /// </summary>
    public required JsonElement State { get; init; }

    /// <summary>Olusturulma zamani (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Son guncellenme zamani (UTC).</summary>
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Kiraci kimligi.</summary>
    public string? TenantId { get; init; }
}

/// <summary>Oturum listesini filtrelemek icin sorgu.</summary>
public sealed record SessionQuery
{
    /// <summary>Yalnizca bu agent'in oturumlarini getirir.</summary>
    public string? AgentName { get; init; }

    /// <summary>Yalnizca bu kiracinin oturumlarini getirir.</summary>
    public string? TenantId { get; init; }

    /// <summary>Atlanacak kayit sayisi.</summary>
    public int Skip { get; init; }

    /// <summary>Getirilecek ust kayit sayisi.</summary>
    public int Take { get; init; } = 50;
}
