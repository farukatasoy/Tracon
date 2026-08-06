namespace AgentPrism;

/// <summary>
/// Her zaman ayni kiraciyi donduren, degismez bir kiraci baglami.
/// </summary>
/// <remarks>
/// <para>
/// Faz 41'de eklendi. Bellek ici depolar kiraciyi <see cref="ITenantContext"/>
/// uzerinden okur; bir depo DI disinda (dogrudan <c>new</c> ile) kuruldugunda
/// baglam verilmezse bu tip devreye girer ve depo tek kiracili davranir.
/// </para>
/// <para>
/// 🚨 Depolarin kiraci baglamini <em>istege bagli</em> tutmasi, filtrelemenin
/// istege bagli oldugu anlamina gelmez. Baglam her zaman vardir; verilmediginde
/// yalnizca degeri sabittir. Filtreleme her kod yolunda calisir.
/// </para>
/// </remarks>
/// <param name="tenantId">Donulecek kiraci kimligi.</param>
public sealed class FixedTenantContext(string tenantId) : ITenantContext
{
    /// <summary>
    /// <see cref="AgentPrismOptions.DefaultTenantId"/>'nin varsayilan degerini
    /// tasiyan paylasilan ornek.
    /// </summary>
    public static FixedTenantContext Default { get; } = new("default");

    /// <inheritdoc />
    public string TenantId { get; } = tenantId;
}
