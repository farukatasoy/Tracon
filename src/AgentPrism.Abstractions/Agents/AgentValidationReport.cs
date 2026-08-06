namespace AgentPrism;

/// <summary>
/// Bir agent tanimini kaydetmeden ve hicbir model cagirmadan derleyen
/// dogrulamanin sonucu.
/// </summary>
/// <remarks>
/// Dogrulama basarisizligi bir HTTP hatasi degildir: istek gecerlidir, cevap
/// "bu tanim gecersiz"dir. Bu yuzden <see cref="Valid"/> <see langword="false"/>
/// olsa bile HTTP yaniti <c>200</c>'dur.
/// </remarks>
public sealed record AgentValidationReport
{
    /// <summary>Tanim hicbir <see cref="ValidationSeverity.Error"/> tasimiyorsa <see langword="true"/>.</summary>
    public required bool Valid { get; init; }

    /// <summary>
    /// Bir denetim, ulasilamayan bir kaynak (ornek: MCP sunucusu) yuzunden tam
    /// sonuclanamadi. <see cref="Valid"/> deger bundan etkilenmez.
    /// </summary>
    public required bool Inconclusive { get; init; }

    /// <summary>Bulunan tum mesajlar. Ilk hatada durulmaz.</summary>
    public required IReadOnlyList<ValidationMessage> Messages { get; init; }
}
