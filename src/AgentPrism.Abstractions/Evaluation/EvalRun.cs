namespace AgentPrism;

/// <summary>
/// Bir <see cref="EvalSuite"/>'in tek bir calistirilma kaydi ve ozeti.
/// </summary>
/// <remarks>
/// <see cref="AgentVersion"/> ve <see cref="ModelId"/> kritiktir: regresyon
/// takibi, ayni suite'in farkli surumlerdeki sonucunu karsilastirmaktir. Ikisi
/// de kosu fiilen baslarken (<see cref="EvalRunStatus.Running"/> gecisinde)
/// doldurulur — tetikleme aninda degil, cunku is kuyrukta beklerken agent
/// tanimi degisebilir.
/// </remarks>
public sealed record EvalRun
{
    /// <summary>Kosu kimligi.</summary>
    public required Guid Id { get; init; }

    /// <summary>Kosunun ait oldugu kiraci.</summary>
    public required string TenantId { get; init; }

    /// <summary>Olculen takimin kimligi.</summary>
    public required Guid SuiteId { get; init; }

    /// <summary>
    /// Bu kosuyu yurutmekten sorumlu is kaydinin kimligi. Kosu, is kuyrugu
    /// (<see cref="IJobStore"/>) uzerinden yurutulur.
    /// </summary>
    public Guid? JobId { get; init; }

    /// <summary>Olculen agent tanim surumu. Kosu baslamadan <see langword="null"/>.</summary>
    public int? AgentVersion { get; init; }

    /// <summary>Olculen model kimligi. Kosu baslamadan <see langword="null"/>.</summary>
    public string? ModelId { get; init; }

    /// <summary>Kosunun guncel durumu.</summary>
    public required EvalRunStatus Status { get; init; }

    /// <summary>Toplam vaka sayisi.</summary>
    public int Total { get; init; }

    /// <summary>Gecen vaka sayisi.</summary>
    public int Passed { get; init; }

    /// <summary>Kalan (basarisiz) vaka sayisi.</summary>
    public int Failed { get; init; }

    /// <summary>Toplam girdi token sayisi.</summary>
    public long? InputTokens { get; init; }

    /// <summary>Toplam cikti token sayisi.</summary>
    public long? OutputTokens { get; init; }

    /// <summary>Baslangic zamani (UTC).</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Bitis zamani (UTC). Kosu surerken <see langword="null"/>.</summary>
    public DateTimeOffset? CompletedAt { get; init; }
}
