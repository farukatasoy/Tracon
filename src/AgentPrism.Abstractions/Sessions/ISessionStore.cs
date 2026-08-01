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

    /// <summary>Oturumu getirir.</summary>
    /// <param name="sessionId">Oturum kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Oturum; yoksa <see langword="null"/>.</returns>
    ValueTask<SessionRecord?> GetAsync(string sessionId, CancellationToken cancellationToken = default);

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
