namespace AgentPrism;

/// <summary>
/// Gecerli istegin kiracisini cozer. Tek kiracili kurulumda sabit bir deger doner
/// ve hicbir ek yapilandirma gerekmez.
/// </summary>
/// <remarks>
/// Cok kiracili senaryolarda (Faz 6) uygulama bu arayuzu kendi kimlik
/// altyapisina baglar: HTTP basligi, claim veya alt alan adi.
/// </remarks>
public interface ITenantContext
{
    /// <summary>Gecerli kiracinin kimligi. Hicbir zaman bos donmez.</summary>
    string TenantId { get; }
}
