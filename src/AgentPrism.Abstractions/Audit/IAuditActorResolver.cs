namespace AgentPrism;

/// <summary>Gecerli caginin aktorunu (kim) cozer.</summary>
/// <remarks>
/// Varsayilan uygulama HTTP istegindeki kullaniciyi okur. Kimlik dogrulamasi
/// yoksa veya aktor cozulemiyorsa <see langword="null"/> doner; bu durum
/// gizlenmez, arayuz "bilinmiyor" gosterir.
/// </remarks>
public interface IAuditActorResolver
{
    /// <summary>Gecerli aktoru cozer.</summary>
    /// <returns>Aktor kimligi; cozulemiyorsa <see langword="null"/>.</returns>
    string? Resolve();
}
