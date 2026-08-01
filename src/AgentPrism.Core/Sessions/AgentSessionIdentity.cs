using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// Bir <see cref="AgentSession"/> nesnesine AgentPrism oturum kimligini damgalar
/// ve geri okur.
/// </summary>
/// <remarks>
/// <para>
/// Kimlik, oturumun <see cref="AgentSession.StateBag"/> alaninda saklanir. Bu alan
/// <c>SerializeSessionAsync</c> ciktisina dahildir; boylece kimlik oturumla birlikte
/// kalicilasir ve geri yuklenen bir oturum kendi kimligini bilir.
/// </para>
/// <para>
/// Durumun oturumda saklanmasinin sebebi Microsoft Agent Framework'un acik uyarisidir:
/// saglayici ve sarmalayici nesneleri tum oturumlar arasinda paylasilir, bu yuzden
/// oturuma ozgu hicbir bilgi alan olarak tutulamaz.
/// </para>
/// </remarks>
public static class AgentSessionIdentity
{
    /// <summary>
    /// Oturum kimliginin saklandigi durum anahtari. Bu deger <strong>kararlidir</strong>;
    /// degistirmek onceden kaydedilmis oturumlari okunamaz hale getirir.
    /// </summary>
    public const string StateKey = "AgentPrism.SessionId";

    /// <summary>Oturuma AgentPrism kimligini yazar.</summary>
    /// <param name="session">Damgalanacak oturum.</param>
    /// <param name="sessionId">Oturum kimligi.</param>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> <see langword="null"/> ise.</exception>
    /// <exception cref="ArgumentException"><paramref name="sessionId"/> bos ise.</exception>
    public static void SetId(AgentSession session, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        session.StateBag.SetValue(StateKey, sessionId, AgentPrismCoreJsonContext.Default.Options);
    }

    /// <summary>Oturumun AgentPrism kimligini okur.</summary>
    /// <param name="session">Okunacak oturum.</param>
    /// <returns>Kimlik; damgalanmamissa <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> <see langword="null"/> ise.</exception>
    public static string? GetId(AgentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return session.StateBag.TryGetValue<string>(StateKey, out var sessionId, AgentPrismCoreJsonContext.Default.Options)
            ? sessionId
            : null;
    }
}
