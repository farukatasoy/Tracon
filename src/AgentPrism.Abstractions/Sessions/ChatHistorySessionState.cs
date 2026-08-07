namespace AgentPrism;

/// <summary>
/// AgentPrism'in bir <c>AgentSession</c> durum cantasinda kullandigi kararli
/// anahtarlar.
/// </summary>
/// <remarks>
/// Bu degerler <strong>kararlidir</strong>; degistirmek mevcut oturumlarin
/// gecmisini koparir.
/// </remarks>
public static class AgentPrismSessionStateKeys
{
    /// <summary>
    /// Sohbet gecmisi saglayicisinin konusma kimligini sakladigi anahtar.
    /// </summary>
    /// <remarks>
    /// Saglayici ornegi <em>tum oturumlarda paylasilir</em> (Microsoft Agent
    /// Framework'un acik uyarisi), bu yuzden konusma kimligi saglayicinin
    /// alaninda degil oturumun kendi durumunda tasinir.
    /// </remarks>
    public const string ChatHistory = "AgentPrism.ChatHistory";
}

/// <summary>
/// Sohbet gecmisi saglayicisinin oturum icinde sakladigi durum.
/// </summary>
/// <remarks>
/// Tip <strong>public</strong>tir cunku iki ayri katman okur: SQL saglayicisi
/// (yazan taraf) ve konusma dallandirmasi (<see cref="IConversationBranchStore"/>
/// ile acilan yeni konusmayi yeni bir oturuma baglayan taraf).
/// </remarks>
public sealed class ChatHistoryState
{
    /// <summary>Bu oturumun mesajlarini tutan konusma kaydinin kimligi.</summary>
    public Guid ConversationId { get; set; }
}
