using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Bir calistirmanin girdi mesajlarini saklar. Yeniden oynatmanin kaynagidir.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 Ayri bir arayuzdur; <see cref="IRunStore"/>'a metot <strong>eklenmez</strong>.
/// Var olan bir arayuze metot eklemek yayindan sonra kiricidir (ayni gerekce
/// <c>IRetentionStore</c> ve <c>IEvalStore</c> icin de gecerlidir).
/// </para>
/// <para>
/// Girdi bugune kadar hicbir yerde kalicilasmiyordu: <see cref="RunRecord"/>
/// girdi tasimaz, <see cref="RunEventType.RunStarted"/> olayi yalnizca ilk
/// kullanici mesajinin <em>metnini</em> tasir (Faz 45) ve o metin kirpilabilir.
/// Yeniden oynatma sadik olmak zorundadir; bu yuzden mesajlar polimorfik
/// icerikleriyle birlikte ayri bir tabloda saklanir.
/// </para>
/// <para>
/// <strong>Deponun hatalari calistirmayi kesmez.</strong> Yazma yolu
/// (<c>RunRecordingAgent</c>) hatayi yakalar ve loglar; gozlemlenebilirlik
/// islevselligi bozmaz.
/// </para>
/// </remarks>
public interface IRunInputStore
{
    /// <summary>Girdi mesajlarini kaydeder.</summary>
    /// <param name="record">Kaydedilecek girdi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    /// <remarks>
    /// Ayni calistirma icin ikinci yazim <strong>yok sayilir</strong>: kuyruga
    /// alinan bir calistirma (Faz 46) ayni kimlikle iki kez baslayabilir ve
    /// girdinin degismemesi gerekir.
    /// </remarks>
    ValueTask SaveAsync(RunInputRecord record, CancellationToken cancellationToken = default);

    /// <summary>Kayitli girdiyi okur.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="runId">Calistirma kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kayit; yoksa veya baska bir kiraciya aitse <see langword="null"/>.</returns>
    ValueTask<RunInputRecord?> GetAsync(
        string tenantId,
        Guid runId,
        CancellationToken cancellationToken = default);
}

/// <summary>Bir calistirmanin kayitli girdisi.</summary>
public sealed record RunInputRecord
{
    /// <summary>Calistirma kimligi.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Calistirmanin ait oldugu kiraci.</summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Girdi mesajlari.
    /// </summary>
    /// <remarks>
    /// 🚨 Polimorfik icerik tasir (<c>TextContent</c>, <c>UriContent</c>,
    /// <c>FunctionResultContent</c> ...). Depoda <c>json</c> sutununda saklanir,
    /// <c>jsonb</c>'de <strong>degil</strong>: <c>jsonb</c> nesne anahtarlarini
    /// yeniden siralar ve System.Text.Json'un <c>$type</c> ayraci nesnenin ilk
    /// ozelligi olmak zorundadir. Gerekce: <c>docs/KARARLAR.md</c>, karar K-027.
    /// </remarks>
    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    /// <summary>Kaydin olusturulma zamani (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}
