using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// Bir agent'in tam tanimi. Kodda tanimlanmis veya veritabaninda saklanmis olmasindan
/// bagimsiz olarak ayni tip kullanilir; kaynak <see cref="Origin"/> ile ayirt edilir.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ToolNames"/> yalnizca <em>ad</em> listesidir, kod degildir. Bir tanim,
/// ancak kodda <c>IToolRegistry</c> icine kayitli bir tool'a isaret edebilir. Bu,
/// AgentPrism'in guvenlik sinirlarindan biridir: arayuzden agent olusturulabilir,
/// ancak calistirilabilir kod tanimlanamaz.
/// </para>
/// <para>
/// Tanim hicbir zaman kimlik bilgisi (API anahtari vb.) tasimaz. Saglayici kimlik
/// bilgileri yapilandirmadan gelir ve veritabanina yazilmaz.
/// </para>
/// </remarks>
public sealed record AgentDefinition
{
    /// <summary>Agent'in benzersiz adi. Katalogda ve API yollarinda anahtar olarak kullanilir.</summary>
    public required string Name { get; init; }

    /// <summary>Arayuzde gosterilecek ad. Bos birakilirsa <see cref="Name"/> kullanilir.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Agent'in ne yaptigini anlatan kisa aciklama.</summary>
    public string? Description { get; init; }

    /// <summary>Modele verilecek sistem talimatlari.</summary>
    public string? Instructions { get; init; }

    /// <summary>Kullanilacak saglayici ve model baglantisi.</summary>
    public required ModelBinding Model { get; init; }

    /// <summary>
    /// Bu agent'in kullanabilecegi tool adlari. Her ad kodda kayitli bir tool'a
    /// karsilik gelmelidir; gelmezse derleme hata verir.
    /// </summary>
    public IReadOnlyList<string> ToolNames { get; init; } = [];

    /// <summary>
    /// Bu agent'in calisma aninda yukleyebilecegi skill adlari. Her ad, kodda
    /// veya skill deposunda bulunan etkin bir skill'e karsilik gelmelidir.
    /// </summary>
    public IReadOnlyList<string> SkillNames { get; init; } = [];

    /// <summary>
    /// Bu agent'in cagirabilecegi diger agent'larin adlari.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Her ad katalogda cozulebilen bir agent'a karsilik gelmelidir. Cagri grafigi
    /// <strong>kaydetme aninda</strong> denetlenir: kendi kendini cagirma ve dolayli
    /// dongu reddedilir.
    /// </para>
    /// <para>
    /// Statik denetim tek basina yeterli degildir - kod tarafindaki bir fabrika
    /// agent'i grafigi tasimaz. Bu yuzden calisma aninda ayrica bir derinlik sayaci
    /// isler (<see cref="AgentRunBudget.MaxDepth"/>).
    /// </para>
    /// <para>
    /// Alt agent <strong>ayni kiracida</strong> calisir ve kiraci degistiremez.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> CallableAgentNames { get; init; } = [];

    /// <summary>
    /// Harness ayarlari. <see langword="null"/> ise sade bir sohbet agent'i uretilir;
    /// dolu ise baglam sikistirma, todo takibi gibi harness yetenekleri devreye girer.
    /// </summary>
    public HarnessSettings? Harness { get; init; }

    /// <summary>
    /// Baglam sikistirma ayarlari. <see langword="null"/> ise hicbir
    /// sikistirma uygulanmaz.
    /// </summary>
    public CompactionSettings? Compaction { get; init; }

    /// <summary>
    /// Bellek saglayicisi ayarlari. <see langword="null"/> ise hicbir bellek
    /// saglayicisi eklenmez.
    /// </summary>
    public MemorySettings? Memory { get; init; }

    /// <summary>Tanimin kaynagi: kod mu, veritabani mi.</summary>
    public AgentDefinitionOrigin Origin { get; init; } = AgentDefinitionOrigin.Database;

    /// <summary>
    /// Tanim surumu. Her kayit islemi bu degeri artirir ve derlenmis agent
    /// onbellegini dogal olarak gecersiz kilar.
    /// </summary>
    public int Version { get; init; } = 1;

    /// <summary>Bu tanimin ait oldugu kiraci. Tek kiracili kurulumda varsayilan deger kullanilir.</summary>
    public string? TenantId { get; init; }

    /// <summary>Tanimin son degistirilme zamani (UTC).</summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Uygulamaya ozgu serbest metadata.</summary>
    public IReadOnlyDictionary<string, JsonElement> Metadata { get; init; }
        = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}
