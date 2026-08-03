namespace AgentPrism;

/// <summary>Bir toplu isin tek bir girdisi ve o girdinin isleme sonucu.</summary>
public sealed record JobItemRecord
{
    /// <summary>Oge kimligi.</summary>
    public required Guid Id { get; init; }

    /// <summary>Ait oldugu is kimligi.</summary>
    public required Guid JobId { get; init; }

    /// <summary>Is icindeki sira numarasi (0'dan baslar).</summary>
    public required int Seq { get; init; }

    /// <summary>Bu ogeye ait girdi metni.</summary>
    public required string Input { get; init; }

    /// <summary>
    /// Bu ogeyi islerken olusan calistirma kaydinin kimligi.
    /// Oge henuz islenmediyse <see langword="null"/>.
    /// </summary>
    public Guid? RunId { get; init; }

    /// <summary>Ogenin isleme durumu.</summary>
    public required JobItemStatus Status { get; init; }

    /// <summary>Basarisizlik mesaji. Yalnizca <see cref="JobItemStatus.Failed"/> durumunda dolu.</summary>
    public string? Error { get; init; }
}
