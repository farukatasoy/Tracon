namespace AgentPrism;

/// <summary>Is listesini filtrelemek icin sorgu.</summary>
public sealed record JobQuery
{
    /// <summary>Yalnizca bu kiracinin islerini getirir.</summary>
    public string? TenantId { get; init; }

    /// <summary>Yalnizca bu turdeki isleri getirir.</summary>
    public JobKind? Kind { get; init; }

    /// <summary>Yalnizca bu durumdaki isleri getirir.</summary>
    public JobStatus? Status { get; init; }

    /// <summary>Yalnizca bu zamanlamanin urettigi isleri getirir.</summary>
    public Guid? ScheduleId { get; init; }

    /// <summary>Atlanacak kayit sayisi.</summary>
    public int Skip { get; init; }

    /// <summary>Getirilecek ust kayit sayisi.</summary>
    public int Take { get; init; } = 50;
}

/// <summary>Bir isi sonlandirmak icin gereken bilgiler.</summary>
public sealed record JobCompletion
{
    /// <summary>Is kimligi.</summary>
    public required Guid JobId { get; init; }

    /// <summary>Son durum.</summary>
    public required JobStatus Status { get; init; }

    /// <summary>Bitis zamani (UTC).</summary>
    public required DateTimeOffset CompletedAt { get; init; }

    /// <summary>Basarisizlik mesaji. Yalnizca <see cref="JobStatus.Failed"/> durumunda dolu.</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>Islenmis bir is ogesinin sonucu. <see cref="IJobStore.ReportItemAsync"/> ile bildirilir.</summary>
public sealed record JobItemResult
{
    /// <summary>Ait oldugu is kimligi.</summary>
    public required Guid JobId { get; init; }

    /// <summary>Ogenin sira numarasi.</summary>
    public required int Seq { get; init; }

    /// <summary>Isleme sonucu.</summary>
    public required JobItemStatus Status { get; init; }

    /// <summary>Olusan calistirma kaydinin kimligi. Uygun degilse <see langword="null"/>.</summary>
    public Guid? RunId { get; init; }

    /// <summary>Basarisizlik mesaji. Yalnizca <see cref="JobItemStatus.Failed"/> durumunda dolu.</summary>
    public string? Error { get; init; }
}
