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

    /// <summary>Calisma aninda yuklenebilecek skill adlari.</summary>
    public IReadOnlyList<string> SkillNames { get; init; } = [];

    /// <summary>
    /// Bu agent'in cagirabilecegi diger agent adlari.
    /// </summary>
    /// <remarks>
    /// Cagri grafigi kaydetme aninda denetlenir: bilinmeyen ad, kendi kendini
    /// cagirma ve dolayli dongu <c>400 Bad Request</c> ile reddedilir.
    /// </remarks>
    public IReadOnlyList<string> CallableAgentNames { get; init; } = [];

    /// <summary>Harness ayarlari. Bos birakilirsa duz sohbet agent'i derlenir.</summary>
    public HarnessSettings? Harness { get; init; }

    /// <summary>Baglam sikistirma ayarlari. Bos birakilirsa sikistirma uygulanmaz.</summary>
    public CompactionSettings? Compaction { get; init; }

    /// <summary>Bellek saglayicisi ayarlari. Bos birakilirsa hicbir bellek saglayicisi eklenmez.</summary>
    public MemorySettings? Memory { get; init; }

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
            SkillNames = SkillNames,
            CallableAgentNames = CallableAgentNames,
            Harness = Harness,
            Compaction = Compaction,
            Memory = Memory,
            Origin = AgentDefinitionOrigin.Database,
        };
}

/// <summary>Skill olusturma veya guncelleme istegi.</summary>
public sealed record AgentSkillRequest
{
    /// <summary>Skill adi.</summary>
    public required string Name { get; init; }

    /// <summary>Skill aciklamasi.</summary>
    public required string Description { get; init; }

    /// <summary>Markdown talimatlari.</summary>
    public required string Instructions { get; init; }

    /// <summary>Uyumluluk bildirimi.</summary>
    public string? Compatibility { get; init; }

    /// <summary>Skill lisansi.</summary>
    public string? License { get; init; }

    /// <summary>MAF frontmatter'indaki izinli tool bildirimi.</summary>
    public string? AllowedTools { get; init; }

    /// <summary>Uygulamaya ozgu metadata.</summary>
    public IReadOnlyDictionary<string, System.Text.Json.JsonElement> Metadata { get; init; }
        = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal);

    /// <summary>Skill etkin mi.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Skill kaynaklari.</summary>
    public IReadOnlyList<AgentSkillResourceDefinition> Resources { get; init; } = [];

    /// <summary>
    /// Skill script'leri.
    /// </summary>
    /// <remarks>
    /// Buraya yazilan icerik <strong>sunucuda calistirilabilir</strong>. Yazmak
    /// tek basina yetmez: script yalnizca <c>AgentPrismSkillScriptOptions</c>
    /// icinde <c>Enabled</c> ve <c>AllowStoredScripts</c> aciksa ve kiraci icin
    /// gecerli bir <c>SkillScriptGrant</c> varsa calisir.
    /// </remarks>
    public IReadOnlyList<AgentSkillScriptDefinition> Scripts { get; init; } = [];

    /// <summary>Istegi kalici skill tanimina cevirir.</summary>
    /// <param name="tenantId">Gecerli kiraci kimligi.</param>
    /// <returns>Kaydedilmeye hazir skill.</returns>
    public AgentSkillDefinition ToDefinition(string tenantId)
        => new()
        {
            TenantId = tenantId,
            Name = Name,
            Description = Description,
            Instructions = Instructions,
            Compatibility = Compatibility,
            License = License,
            AllowedTools = AllowedTools,
            Metadata = Metadata,
            Enabled = Enabled,
            Resources = Resources,
            Scripts = Scripts,
        };
}

/// <summary>Script calistirma izni verme istegi.</summary>
/// <remarks>
/// Izin vermek, bu kiracinin adina sunucuda kod calistirilmasina yetki vermektir.
/// Bu yuzden ilgili uc yalnizca yonetici rolune aciktir ve her istek denetim
/// izine yazilir.
/// </remarks>
public sealed record SkillScriptGrantRequest
{
    /// <summary>Izin verilen skill'in adi.</summary>
    public required string SkillName { get; init; }

    /// <summary>
    /// Izin verilen script'in adi. <see langword="null"/> ise skill'in tum
    /// script'leri kapsanir.
    /// </summary>
    public string? ScriptName { get; init; }

    /// <summary>Iznin bitis zamani. <see langword="null"/> ise sinirsizdir.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
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

/// <summary>
/// Iki tanim surumunun ham JSON yaniti (Faz 19.1). Diff hesabi sunucuda yapilmaz;
/// istemci iki ham tanimi alan alan karsilastirir.
/// </summary>
public sealed record AgentVersionDiffResponse
{
    /// <summary>Karsilastirmanin sol (genelde eski) tarafi.</summary>
    public required AgentDefinition Left { get; init; }

    /// <summary>Karsilastirmanin sag (genelde yeni) tarafi.</summary>
    public required AgentDefinition Right { get; init; }
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

    /// <summary>
    /// Onceden <c>POST /api/attachments</c> ile yuklenmis eklerin kimlikleri.
    /// </summary>
    /// <remarks>
    /// Her kimlik cagiran kiraciya ait olmalidir; aksi halde istek <c>400</c> ile
    /// reddedilir. Ikili icerik mesajda tasinmaz, yalniz kucuk bir referans
    /// (<see cref="Microsoft.Extensions.AI.UriContent"/>) eklenir.
    /// Gerekce: <c>docs/14-COK-MODLULUK.md</c>, bolum 14.1 ve 14.4.
    /// </remarks>
    public IReadOnlyList<Guid> AttachmentIds { get; init; } = [];
}

/// <summary>
/// Kuyruga alinmis bir calistirmanin <c>202 Accepted</c> yaniti (Faz 46).
/// </summary>
/// <remarks>
/// <c>Prefer: respond-async</c> basligiyla baslatilan bir calistirmada
/// donulur. Ayni bilgiler <c>Location</c> basliginda da tasinir; govde
/// istemcinin ayrica bir olay akisi adresi (<see cref="EventsLocation"/>)
/// kurmasina gerek birakmaz.
/// </remarks>
public sealed record AcceptedRunResponse
{
    /// <summary>Calistirma kimligi.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Isi tasiyan kuyruk kaydinin kimligi.</summary>
    public required Guid JobId { get; init; }

    /// <summary>Calistirma kaydinin adresi. <c>Location</c> basligiyla aynidir.</summary>
    public required string Location { get; init; }

    /// <summary>Olay akisinin adresi.</summary>
    public required string EventsLocation { get; init; }
}
