namespace AgentPrism;

/// <summary>
/// Bir workflow tanimin <c>jsonb</c> sutununa yazilan bolumu.
/// </summary>
/// <remarks>
/// <para>
/// Ad, surum, kiraci ve guncelleme zamani <em>sutunlarda</em> tutulur ve tek
/// dogru kaynak orasidir. Bu alanlarin ayrica <c>jsonb</c> icinde tekrarlanmasi
/// iki kayit noktasi olustururdu.
/// </para>
/// <para>
/// 🚨 <strong>Bu DTO elle yazilmistir ve <see cref="WorkflowDefinition"/> ile
/// otomatik senkronize DEGILDIR.</strong> Tanima yeni bir alan eklendiginde
/// buraya da eklenmelidir; eklenmezse alan PostgreSQL'e <em>sessizce</em>
/// yazilmaz. Ne derleme ne test kirilir - yalnizca gidis-donus testi yakalar.
/// Ayni tuzak Faz 13'te <c>AgentDefinitionPayload</c> ile yasandi.
/// </para>
/// </remarks>
internal sealed record WorkflowDefinitionPayload
{
    /// <summary>Arayuzde gosterilecek ad.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Workflow aciklamasi.</summary>
    public string? Description { get; init; }

    /// <summary>Kullanilan hazir desen.</summary>
    public WorkflowKind Kind { get; init; }

    /// <summary>Grafa giren agent adlari.</summary>
    public IReadOnlyList<string> AgentNames { get; init; } = [];

    /// <summary>Yonetici agent'in adi.</summary>
    public string? ManagerAgentName { get; init; }

    /// <summary>En fazla tur sayisi.</summary>
    public int? MaxIterations { get; init; }

    /// <summary>Devretme talimati.</summary>
    public string? HandoffInstructions { get; init; }

    /// <summary>Yonetici agent'in plani insana onaylatilsin mi.</summary>
    public bool RequirePlanApproval { get; init; }

    /// <summary>Tanimin icerigini yuke donusturur.</summary>
    /// <param name="definition">Kaynak tanim.</param>
    /// <returns>Serilestirilecek yuk.</returns>
    public static WorkflowDefinitionPayload FromDefinition(WorkflowDefinition definition)
        => new()
        {
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            Kind = definition.Kind,
            AgentNames = definition.AgentNames,
            ManagerAgentName = definition.ManagerAgentName,
            MaxIterations = definition.MaxIterations,
            HandoffInstructions = definition.HandoffInstructions,
            RequirePlanApproval = definition.RequirePlanApproval,
        };

    /// <summary>Yuku sutunlardan gelen bilgilerle birlestirip tanimi kurar.</summary>
    /// <param name="name">Workflow adi.</param>
    /// <param name="version">Surum numarasi.</param>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="updatedAt">Son guncelleme zamani.</param>
    /// <returns>Tam tanim.</returns>
    public WorkflowDefinition ToDefinition(string name, int version, string tenantId, DateTimeOffset updatedAt)
        => new()
        {
            Name = name,
            DisplayName = DisplayName,
            Description = Description,
            Kind = Kind,
            AgentNames = AgentNames,
            ManagerAgentName = ManagerAgentName,
            MaxIterations = MaxIterations,
            HandoffInstructions = HandoffInstructions,
            RequirePlanApproval = RequirePlanApproval,
            TenantId = tenantId,
            Version = version,
            UpdatedAt = updatedAt,
        };
}
