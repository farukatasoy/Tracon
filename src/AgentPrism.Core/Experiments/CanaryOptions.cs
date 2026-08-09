namespace AgentPrism;

/// <summary>Kanarya degerlendirme arka plan servisinin ayarlari — Faz 56.</summary>
/// <remarks>
/// <c>AgentPrism:Canary</c> yapilandirma bolumunden okunur.
/// </remarks>
public sealed class CanaryOptions
{
    /// <summary>Yapilandirma bolumu adi.</summary>
    public const string SectionName = "AgentPrism:Canary";

    /// <summary>
    /// 🚨 Otomatik geri alma etkin mi. Varsayilan <see langword="false"/> (K1) —
    /// acilmadan hicbir deney kendiliginden durmaz veya agirligi degismez, bir
    /// <see cref="CanaryPolicy"/> tanimlansa bile.
    /// </summary>
    public bool AutoRollbackEnabled { get; set; }

    /// <summary>Kanarya kurali tanimli calisan deneylerin ne siklikla degerlendirildigi.</summary>
    public TimeSpan ScanInterval { get; set; } = TimeSpan.FromMinutes(5);
}
