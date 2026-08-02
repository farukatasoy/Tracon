namespace AgentPrism;

/// <summary>Bir workflow tanimini kaydetme istegi.</summary>
/// <remarks>
/// Ad <em>yoldan</em> gelir, govdeden degil. Iki kaynak olmasi, ikisinin
/// celismesi durumunda hangisinin kazandigini sormaya yol acardi.
/// </remarks>
public sealed record WorkflowSaveRequest
{
    /// <summary>Arayuzde gosterilecek ad.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Workflow'un ne yaptigini anlatan kisa aciklama.</summary>
    public string? Description { get; init; }

    /// <summary>Kullanilacak hazir desen.</summary>
    public WorkflowKind Kind { get; init; }

    /// <summary>Grafa girecek agent adlari.</summary>
    public IReadOnlyList<string>? AgentNames { get; init; }

    /// <summary>Yonetici agent'in adi. Yalnizca <see cref="WorkflowKind.Magentic"/> icin.</summary>
    public string? ManagerAgentName { get; init; }

    /// <summary>En fazla tur sayisi.</summary>
    public int? MaxIterations { get; init; }

    /// <summary>Devretme talimati. Yalnizca <see cref="WorkflowKind.Handoff"/> icin.</summary>
    public string? HandoffInstructions { get; init; }
}

/// <summary>Bir workflow'u calistirma istegi.</summary>
public sealed record WorkflowRunHttpRequest
{
    /// <summary>Grafa girecek kullanici mesaji.</summary>
    public string? Message { get; init; }

    /// <summary>
    /// Yurutme oturumunun kimligi. Bos birakilirsa uretilir. Kontrol noktalari
    /// bu deger altinda gruplanir.
    /// </summary>
    public string? SessionId { get; init; }
}

/// <summary>Bir workflow'u kontrol noktasindan sürdürme istegi.</summary>
public sealed record WorkflowResumeHttpRequest
{
    /// <summary>
    /// Devam edilecek kontrol noktasinin kimligi. Bos birakilirsa calistirmanin
    /// en son kontrol noktasi kullanilir.
    /// </summary>
    public string? CheckpointId { get; init; }
}
