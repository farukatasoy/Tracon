namespace AgentPrism;

/// <summary>Bir agent tanimi dogrulamasinin urettigi tek bir bulgu.</summary>
/// <remarks>
/// <see cref="Message"/> sunucudan gelir ve cevrilmez (K-232); arayuz yalnizca
/// <see cref="Code"/> alanina gore kendi baslıgini gosterir.
/// </remarks>
public sealed record ValidationMessage
{
    /// <summary>Bulgunun onem derecesi.</summary>
    public required ValidationSeverity Severity { get; init; }

    /// <summary>
    /// Makine tarafindan okunabilir kararli kod. Ornek: <c>unknown_tool</c>, <c>cycle</c>.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>Insan tarafindan okunabilir aciklama. Cevrilmez.</summary>
    public required string Message { get; init; }

    /// <summary>
    /// Sorunlu alanin tanim icindeki yolu. Ornek: <c>toolNames[2]</c>.
    /// Bir alana isaret etmiyorsa <see langword="null"/>.
    /// </summary>
    public string? Path { get; init; }
}
