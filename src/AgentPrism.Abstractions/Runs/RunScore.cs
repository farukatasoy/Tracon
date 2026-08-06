namespace AgentPrism;

/// <summary>
/// Bir calistirmaya veya calistirma icindeki tek bir mesaja iliskin insan
/// (veya yargic) puani.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MessageId"/> bos ise puan <strong>tum calistirmaya</strong>
/// aittir; doluysa tek bir mesaja aittir.
/// </para>
/// <para>
/// Bir yazar (<see cref="Author"/>) ayni hedefi (calistirma veya mesaj) bir
/// kez puanlar — ikinci yazim mevcut satiri gunceller,
/// <see cref="IRunScoreStore.UpsertAsync"/>. <see cref="Author"/> bos
/// oldugunda (kimliksiz kurulum) bu kural uygulanmaz; her cagri yeni bir
/// satir acar.
/// </para>
/// </remarks>
public sealed record RunScore
{
    /// <summary>Puanin kimligi.</summary>
    public Guid Id { get; init; }

    /// <summary>Kiraci.</summary>
    public required string TenantId { get; init; }

    /// <summary>Puanlanan calistirmanin kimligi.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Puanlanan mesajin kimligi. Bos ise puan tum calistirmaya aittir.</summary>
    public string? MessageId { get; init; }

    /// <summary><see cref="Value"/>'nun bicimi.</summary>
    public required RunScoreKind Kind { get; init; }

    /// <summary>Puan degeri. <see cref="RunScoreKind.Binary"/> icin 0/1, <see cref="RunScoreKind.Stars"/> icin 1..5.</summary>
    public required int Value { get; init; }

    /// <summary>Serbest metin yorum.</summary>
    public string? Comment { get; init; }

    /// <summary>
    /// Puanin kaynagi: <c>human</c>, <c>api</c> veya <c>judge</c>. Bugun tek
    /// deger <c>human</c>'dir; sutun cevrimici degerlendirmenin (F-71) yargic
    /// puanini aynen bu tabloya yazabilmesi icin bastan konur.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>Puani veren aktor. Kimliksiz kurulumda <see langword="null"/>.</summary>
    public string? Author { get; init; }

    /// <summary>Olusturulma/son guncellenme zamani.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}
