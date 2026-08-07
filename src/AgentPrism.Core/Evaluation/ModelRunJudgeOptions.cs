namespace AgentPrism;

/// <summary>Yerlesik model tabanli yargicin (<see cref="ModelRunJudge"/>) ayarlari — Faz 49.</summary>
/// <remarks>
/// Yapilandirma bolumunden okunmaz — <see cref="ModelBinding"/> ic ice bir tip
/// oldugu icin bu proje bunu kod tarafinda kurar
/// (<see cref="AgentPrismOnlineEvaluationBuilderExtensions.AddModelRunJudge"/>).
/// </remarks>
public sealed class ModelRunJudgeOptions
{
    /// <summary>
    /// Yargicin modeli.
    /// </summary>
    /// <remarks>
    /// 🚨 Olculen agent'in modelinden AYRI secilir; ucuz bir model yeterlidir
    /// ve maliyeti dusurur. Ayni model kendi ciktisini puanlarken sistematik
    /// olarak yanli olur — bu yuzden zorunlu bir ayardir, varsayilani yoktur.
    /// </remarks>
    public required ModelBinding Model { get; set; }

    /// <summary>Puanlama olcutleri. Istemin govdesini olusturur.</summary>
    public IList<string> Criteria { get; } = [];

    /// <summary>Yargica verilen ek talimat.</summary>
    public string? Instructions { get; set; }
}
