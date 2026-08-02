using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// Calisma aninda bir agent'a yuklenebilen markdown tabanli skill tanimi.
/// </summary>
/// <remarks>
/// Skill talimat, kaynak ve <see cref="Scripts"/> tasir. Script calistirma ayri
/// bir guvenlik siniridir: <c>AgentPrismSkillScriptOptions.AllowStoredScripts</c>
/// acilmadikca kayitli script'ler yalniz saklanir, hicbir zaman calistirilmaz.
/// </remarks>
public sealed record AgentSkillDefinition
{
    /// <summary>Skill'in benzersiz kimligi.</summary>
    public Guid Id { get; init; }

    /// <summary>Skill'in ait oldugu kiraci.</summary>
    public required string TenantId { get; init; }

    /// <summary>Skill adi. Agent tanimlari bu adla skill'e baglanir.</summary>
    public required string Name { get; init; }

    /// <summary>Skill'in kisa aciklamasi.</summary>
    public required string Description { get; init; }

    /// <summary>Modele verilecek markdown talimatlari.</summary>
    public required string Instructions { get; init; }

    /// <summary>Skill'in uyumluluk bildirimi.</summary>
    public string? Compatibility { get; init; }

    /// <summary>Skill lisansi.</summary>
    public string? License { get; init; }

    /// <summary>MAF frontmatter'indaki izinli tool bildirimi.</summary>
    public string? AllowedTools { get; init; }

    /// <summary>Uygulamaya ozgu serbest metadata.</summary>
    public IReadOnlyDictionary<string, JsonElement> Metadata { get; init; }
        = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    /// <summary>Skill'in derlemeye alinip alinmayacagini belirtir.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Skill'in kayit surumu.</summary>
    public int Version { get; init; } = 1;

    /// <summary>Skill kaynaklari.</summary>
    public IReadOnlyList<AgentSkillResourceDefinition> Resources { get; init; } = [];

    /// <summary>
    /// Skill'in veritabaninda saklanan script'leri.
    /// </summary>
    /// <remarks>
    /// Bu script'ler sunucuda calisir. Modele ancak
    /// <c>AgentPrismSkillScriptOptions.AllowStoredScripts</c> acikken gorunur;
    /// kapaliyken kayit saklanir ama calistirilamaz.
    /// </remarks>
    public IReadOnlyList<AgentSkillScriptDefinition> Scripts { get; init; } = [];

    /// <summary>Skill'in olusturulma zamani.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Skill'in son guncellenme zamani.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Bir <see cref="AgentSkillDefinition"/> ile tasinan okunabilir kaynak.</summary>
public sealed record AgentSkillResourceDefinition
{
    /// <summary>Skill icinde benzersiz kaynak adi.</summary>
    public required string Name { get; init; }

    /// <summary>Kaynak aciklamasi.</summary>
    public string? Description { get; init; }

    /// <summary>Kaynak medya tipi.</summary>
    public string MediaType { get; init; } = "text/plain";

    /// <summary>Kaynak metin icerigi.</summary>
    public required string Content { get; init; }
}

/// <summary>
/// Bir <see cref="AgentSkillDefinition"/> ile birlikte saklanan, sunucuda
/// calistirilabilen script.
/// </summary>
/// <remarks>
/// <strong>Bu icerik sunucuda calisir.</strong> Kaydi olusturmak calistirma izni
/// vermez: calistirma icin kod tarafinda
/// <c>AgentPrismSkillScriptOptions.AllowStoredScripts</c> acilmis olmali ve bir
/// <see cref="SkillScriptGrant"/> kaydi bulunmalidir.
/// </remarks>
public sealed record AgentSkillScriptDefinition
{
    /// <summary>Skill icinde benzersiz script adi.</summary>
    public required string Name { get; init; }

    /// <summary>Script'in ne yaptigini modele anlatan aciklama.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// Dosya uzantisi (nokta olmadan, ornegin <c>py</c>). Yorumlayici bu deger
    /// uzerinden beyaz listeden secilir.
    /// </summary>
    public required string Extension { get; init; }

    /// <summary>Script'in kaynak metni.</summary>
    public required string Content { get; init; }

    /// <summary>
    /// Modele bildirilecek arguman semasi. Gecerli bir JSON Schema nesnesi
    /// olmalidir; <see langword="null"/> ise script argumansiz cagrilir.
    /// </summary>
    public string? ParametersSchema { get; init; }
}
