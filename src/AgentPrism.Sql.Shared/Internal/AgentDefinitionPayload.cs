using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// Bir agent tanimin <c>jsonb</c> sutununa yazilan bolumu.
/// </summary>
/// <remarks>
/// <para>
/// Ad, surum, kiraci, kaynak ve guncelleme zamani <em>sutunlarda</em> tutulur ve
/// tek dogru kaynak orasidir. Bu alanlarin ayrica <c>jsonb</c> icinde tekrarlanmasi
/// iki kayit noktasi olustururdu; surum artisi sonrasi jsonb'nin yeniden yazilmasi
/// gerekir ve tutarsizlik riski dogardi.
/// </para>
/// <para>
/// Bu yuzden yalnizca tanimin <em>icerigi</em> serilestirilir; geri okurken
/// sutunlarla birlestirilir.
/// </para>
/// </remarks>
internal sealed record AgentDefinitionPayload
{
    /// <summary>Arayuzde gosterilecek ad.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Agent aciklamasi.</summary>
    public string? Description { get; init; }

    /// <summary>Sistem talimatlari.</summary>
    public string? Instructions { get; init; }

    /// <summary>Saglayici ve model baglantisi.</summary>
    public required ModelBinding Model { get; init; }

    /// <summary>Kullanilabilecek tool adlari.</summary>
    public IReadOnlyList<string> ToolNames { get; init; } = [];

    /// <summary>Calisma aninda yuklenebilecek skill adlari.</summary>
    public IReadOnlyList<string> SkillNames { get; init; } = [];

    /// <summary>Bu agent'in cagirabilecegi diger agent adlari.</summary>
    public IReadOnlyList<string> CallableAgentNames { get; init; } = [];

    /// <summary>Harness ayarlari.</summary>
    public HarnessSettings? Harness { get; init; }

    /// <summary>Baglam sikistirma ayarlari.</summary>
    public CompactionSettings? Compaction { get; init; }

    /// <summary>Bellek saglayicisi ayarlari.</summary>
    public MemorySettings? Memory { get; init; }

    /// <summary>Uygulamaya ozgu serbest metadata.</summary>
    public Dictionary<string, JsonElement>? Metadata { get; init; }

    /// <summary>Tanimin icerigini yuke donusturur.</summary>
    /// <param name="definition">Kaynak tanim.</param>
    /// <returns>Serilestirilecek yuk.</returns>
    public static AgentDefinitionPayload FromDefinition(AgentDefinition definition)
        => new()
        {
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            Instructions = definition.Instructions,
            Model = definition.Model,
            ToolNames = definition.ToolNames,
            SkillNames = definition.SkillNames,
            CallableAgentNames = definition.CallableAgentNames,
            Harness = definition.Harness,
            Compaction = definition.Compaction,
            Memory = definition.Memory,
            Metadata = definition.Metadata.Count == 0
                ? null
                : new Dictionary<string, JsonElement>(definition.Metadata, StringComparer.Ordinal),
        };

    /// <summary>Yuku sutun degerleriyle birlestirerek tam tanimi kurar.</summary>
    /// <param name="name">Agent adi.</param>
    /// <param name="version">Surum numarasi.</param>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="updatedAt">Son guncelleme zamani.</param>
    /// <returns>Tam tanim.</returns>
    public AgentDefinition ToDefinition(string name, int version, string tenantId, DateTimeOffset updatedAt)
        => new()
        {
            Name = name,
            DisplayName = DisplayName,
            Description = Description,
            Instructions = Instructions,
            Model = Model,
            ToolNames = ToolNames,
            SkillNames = SkillNames,
            CallableAgentNames = CallableAgentNames,
            Harness = Harness,
            Compaction = Compaction,
            Memory = Memory,
            Origin = AgentDefinitionOrigin.Database,
            Version = version,
            TenantId = tenantId,
            UpdatedAt = updatedAt,
            Metadata = Metadata is null
                ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                : new Dictionary<string, JsonElement>(Metadata, StringComparer.Ordinal),
        };
}

/// <summary>
/// <see cref="SqlChatHistoryProvider"/> tarafindan oturum icinde saklanan durum.
/// </summary>
/// <remarks>
/// Saglayici ornegi tum oturumlarda paylasilir; bu yuzden konusma kimligi
/// saglayicida degil, oturumun kendi durumunda tasinir.
/// </remarks>
internal sealed class ChatHistoryState
{
    /// <summary>Bu oturumun mesajlarini tutan konusma kaydinin kimligi.</summary>
    public Guid ConversationId { get; set; }
}
