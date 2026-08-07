namespace AgentPrism;

/// <summary>Tek yurutucu secimi (Faz 42) ayarlari.</summary>
/// <remarks>
/// <c>AgentPrism:SingletonExecution</c> yapilandirma bolumunden okunur. Bkz.
/// <c>AgentPrismServiceCollectionExtensions.AddAgentPrism</c>.
/// </remarks>
public sealed class SingletonExecutionOptions
{
    /// <summary>Yapilandirma bolumu adi.</summary>
    public const string SectionName = "AgentPrism:SingletonExecution";

    /// <summary>
    /// Tek yurutucu secimi acik mi. Varsayilan <see langword="false"/>:
    /// tek ornekli kurulumda davranis degismez ve kira tablosuna hicbir
    /// sorgu gitmez.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Kira suresi. Yenileme bu surenin UCTE BIRI araliginda yapilir; yarisi
    /// secilirse tek bir kacirilmis yenileme kirayi dusurur.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Bu ornegin kimligi. <see langword="null"/> veya bos ise kendiliginden
    /// (makine adi + surec kimligi + benzersiz bir uzanti ile) uretilir.
    /// </summary>
    public string? OwnerId { get; set; }
}
