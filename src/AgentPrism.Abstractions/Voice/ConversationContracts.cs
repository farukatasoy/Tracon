using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>Bir gercek zamanli konusma baglantisinin ozet kaydi.</summary>
/// <remarks>
/// <para>
/// 🚨 Ses <strong>icerigi</strong> bu kayitta durmaz. Kayit yalnizca olcum ve
/// gozlemlenebilirlik icindir: kim, ne zaman, kac tur, ne kadar ses. Ses
/// saklaniyorsa (varsayilan <em>hayir</em>) baytlar <c>attachments</c>
/// tablosundadir.
/// </para>
/// <para>
/// Her konusma turu ayrica normal bir <c>runs</c> satiri uretir. Ses,
/// calistirma yolunu degistirmez; yalnizca girdi ve cikti bicimini degistirir.
/// </para>
/// </remarks>
public sealed record VoiceSessionRecord
{
    /// <summary>Baglantinin kimligi.</summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Kiraci. 🚨 Baglanti kurulurken cozulur ve baglanti boyunca
    /// <strong>sabittir</strong>.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>Konusmanin yurudugu agent oturumunun kimligi.</summary>
    public required string SessionId { get; init; }

    /// <summary>Konusulan agent'in adi.</summary>
    public required string AgentName { get; init; }

    /// <summary>Baglantinin acildigi an.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Baglantinin kapandigi an; hala acikken <see langword="null"/>.</summary>
    public DateTimeOffset? EndedAt { get; init; }

    /// <summary>Tamamlanan konusma turu sayisi.</summary>
    public int Turns { get; init; }

    /// <summary>
    /// Cozulen toplam ses suresi (saniye). Saglayici sure bildirmediyse
    /// <see langword="null"/> kalir — AgentPrism sure uydurmaz (K-032).
    /// </summary>
    public decimal? InputSeconds { get; init; }

    /// <summary>Seslendirilen toplam karakter sayisi.</summary>
    public long? OutputChars { get; init; }

    /// <summary>Baglantinin nicin kapandigi.</summary>
    public VoiceSessionEndReason? EndReason { get; init; }

    /// <summary>Baglantiyi acan aktor.</summary>
    public string? CreatedBy { get; init; }
}

/// <summary>Bir konusma baglantisinin kapanma nedeni.</summary>
/// <remarks>
/// JSON'da <strong>ad olarak</strong> yazilir; veritabaninda <c>smallint</c>
/// olarak saklanir. Sayisal degerler <strong>kararlidir</strong> ve
/// degistirilemez.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<VoiceSessionEndReason>))]
public enum VoiceSessionEndReason
{
    /// <summary>Istemci <c>stop</c> gonderdi veya soketi duzgun kapatti.</summary>
    Client = 0,

    /// <summary>Baglanti boste kaldi ve zaman asimina ugradi.</summary>
    IdleTimeout = 1,

    /// <summary>Baglanti izin verilen en uzun sureye ulasti.</summary>
    DurationLimit = 2,

    /// <summary>Bir hata baglantiyi kapatti.</summary>
    Error = 3,

    /// <summary>Sunucu kapaniyor.</summary>
    ServerShutdown = 4,
}

/// <summary>Konusma kayitlarini listeleme suzgeci.</summary>
public sealed record VoiceSessionQuery
{
    /// <summary>Agent adi suzgeci; bos ise tum agent'lar.</summary>
    public string? AgentName { get; init; }

    /// <summary>Oturum kimligi suzgeci; bos ise tum oturumlar.</summary>
    public string? SessionId { get; init; }

    /// <summary>Atlanacak kayit sayisi.</summary>
    public int Skip { get; init; }

    /// <summary>Dondurulecek en fazla kayit sayisi.</summary>
    public int Take { get; init; } = 50;
}

/// <summary>Gercek zamanli konusma baglantilarinin ozet kaydini saklar.</summary>
/// <remarks>
/// <para>
/// Depo <strong>gozlemlenebilirlik icindir</strong> ve islevselligi bozmaz: bir
/// yazma hatasi konusmayi kesmez, yalnizca gunluge yazilir.
/// </para>
/// <para>
/// Sozlesme <c>AgentPrism.Abstractions</c>'ta yasar cunku HTTP katmani
/// konusma uclarini sunarken bu tipleri gorur (K-174 deseni).
/// </para>
/// </remarks>
public interface IVoiceSessionStore
{
    /// <summary>Bir konusma kaydini ekler veya gunceller.</summary>
    /// <param name="record">Kayit.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    ValueTask SaveAsync(VoiceSessionRecord record, CancellationToken cancellationToken = default);

    /// <summary>Bir kiracinin konusma kayitlarini en yeniden eskiye listeler.</summary>
    /// <param name="tenantId">Kiraci.</param>
    /// <param name="query">Suzgec.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kayitlar.</returns>
    ValueTask<IReadOnlyList<VoiceSessionRecord>> QueryAsync(
        string tenantId,
        VoiceSessionQuery query,
        CancellationToken cancellationToken = default);
}
