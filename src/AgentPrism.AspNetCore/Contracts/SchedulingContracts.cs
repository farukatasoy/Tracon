using System.Text.Json;

namespace AgentPrism;

/// <summary>Bir zamanlama olusturma/guncelleme istegi.</summary>
/// <remarks>
/// Ad <em>yoldan</em> gelir, govdeden degil — <see cref="WorkflowSaveRequest"/>
/// ile ayni gerekce.
/// </remarks>
public sealed record JobScheduleSaveRequest
{
    /// <summary>Bu zamanlamanin urettigi isin turu.</summary>
    public required JobKind Kind { get; init; }

    /// <summary>Calistirilacak agent veya workflow adi.</summary>
    public required string TargetName { get; init; }

    /// <summary>
    /// Bes alanli cron ifadesi. Bos birakilirsa zamanlama yalnizca elle
    /// (<c>POST .../trigger</c>) tetiklenir.
    /// </summary>
    public string? Cron { get; init; }

    /// <summary><see cref="Cron"/> ifadesinin yorumlanacagi saat dilimi.</summary>
    public string TimeZone { get; init; } = "UTC";

    /// <summary>Girdi kumesi veya parametreler.</summary>
    public JsonElement Payload { get; init; }

    /// <summary>Zamanlama etkin mi.</summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>Bir zamanlamayi hemen tetikleme istegi.</summary>
public sealed record JobTriggerRequest
{
    /// <summary>
    /// Bu calistirma icin kullanilacak yuk. Bos birakilirsa zamanlamanin kendi
    /// yuku kullanilir.
    /// </summary>
    public JsonElement? Payload { get; init; }
}

/// <summary>Tek bir isin ayrintili gorunumu: kayit ve ogeleri birlikte.</summary>
public sealed record JobDetailResponse
{
    /// <summary>Is kaydi.</summary>
    public required JobRecord Job { get; init; }

    /// <summary>Isin ogeleri, sira numarasina gore.</summary>
    public required IReadOnlyList<JobItemRecord> Items { get; init; }
}
