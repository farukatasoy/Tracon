namespace AgentPrism;

/// <summary>
/// Agent tanimi olusturma ve guncelleme istegi.
/// </summary>
/// <remarks>
/// <see cref="AgentDefinition"/> dogrudan baglanmaz. Tanimin <c>Origin</c>,
/// <c>Version</c>, <c>TenantId</c> ve <c>UpdatedAt</c> alanlari sunucuya aittir;
/// istemcinin bunlari belirlemesine izin vermek surum gecmisini ve kiraci
/// yalitimini bozardi.
/// </remarks>
public sealed record AgentDefinitionRequest
{
    /// <summary>Agent adi. Katalogda benzersizdir.</summary>
    public required string Name { get; init; }

    /// <summary>Arayuzde gosterilecek ad.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Kisa aciklama.</summary>
    public string? Description { get; init; }

    /// <summary>Sistem talimati.</summary>
    public string? Instructions { get; init; }

    /// <summary>Model baglantisi: saglayici, model ve ornekleme ayarlari.</summary>
    public required ModelBinding Model { get; init; }

    /// <summary>
    /// Kullanilacak tool adlari. Tool'lar yalnizca kodda tanimlanir; burada
    /// yalnizca kayitli bir tool'un adi verilebilir.
    /// </summary>
    public IReadOnlyList<string> ToolNames { get; init; } = [];

    /// <summary>Harness ayarlari. Bos birakilirsa duz sohbet agent'i derlenir.</summary>
    public HarnessSettings? Harness { get; init; }

    /// <summary>Istegi kalici bir tanima cevirir.</summary>
    /// <returns>Veritabanina yazilabilir tanim.</returns>
    public AgentDefinition ToDefinition()
        => new()
        {
            Name = Name,
            DisplayName = DisplayName,
            Description = Description,
            Instructions = Instructions,
            Model = Model,
            ToolNames = ToolNames,
            Harness = Harness,
            Origin = AgentDefinitionOrigin.Database,
        };
}

/// <summary>
/// Tek bir agent'in ayrintili gorunumu.
/// </summary>
/// <remarks>
/// Katalog hem kodda hem veritabaninda tanimli agent'lari gosterir. Kodda
/// tanimlananlar <strong>duzenlenemez</strong>: ad cakismasinda kod kazanir
/// (karar K-003), dolayisiyla veritabanina yazilan bir tanim hicbir zaman
/// cozulmezdi. <see cref="IsEditable"/> arayuzun bunu onceden bilmesini saglar.
/// </remarks>
public sealed record AgentDetailResponse
{
    /// <summary>Katalog ozeti.</summary>
    public required AgentDescriptor Descriptor { get; init; }

    /// <summary>Kalici tanim. Agent yalnizca kodda tanimliysa <see langword="null"/>.</summary>
    public AgentDefinition? Definition { get; init; }

    /// <summary>Bu agent yonetim API'sinden degistirilebilir mi.</summary>
    public required bool IsEditable { get; init; }
}

/// <summary>Bir tanimi onceki bir surume dondurme istegi.</summary>
public sealed record AgentRollbackRequest
{
    /// <summary>Donulecek surum numarasi.</summary>
    public required int Version { get; init; }
}

/// <summary>Arayuzden yapilan deneme calistirmasinin istegi.</summary>
public sealed record AgentRunRequest
{
    /// <summary>
    /// Kullanici mesaji. Yalnizca <see cref="Approvals"/> gonderiliyorsa bos birakilabilir.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Oturum kimligi. Verilmezse calistirma oturumsuzdur ve gecmis tasinmaz.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Bekleyen tool cagrilarina verilen onay kararlari.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Onay <strong>bir sonraki turun girdisidir</strong>: Microsoft Agent Framework
    /// bekleyen bir cagriyi yanitta <c>ToolApprovalRequestContent</c> olarak dondurur
    /// ve karari sonraki calistirmanin mesajlarinda bekler. Ayri bir "devam et"
    /// ucu bu yuzden yoktur.
    /// </para>
    /// <para>
    /// Kararlar yalnizca <see cref="SessionId"/> verildiginde islenir: bekleyen
    /// istek oturum gecmisinde yasar ve oturumsuz bir calistirmada bulunamaz.
    /// </para>
    /// </remarks>
    public IReadOnlyList<ToolApprovalDecision> Approvals { get; init; } = [];
}
