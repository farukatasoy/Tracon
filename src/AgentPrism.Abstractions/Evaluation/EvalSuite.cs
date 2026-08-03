using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// Bir agent icin tanimlanmis degerlendirme (eval) takimi: hangi agent'in,
/// hangi denetimlerle olculecegini tasir.
/// </summary>
/// <remarks>
/// <see cref="Checks"/> bildirimseldir (K2): serbest kod calistirmaz, yalnizca
/// <c>EvalCheckFactory</c>'nin taninan tur adlarina esledigi bir JSON dizisidir.
/// Ozel bir denetim gerekiyorsa kod tarafinda <c>AddEvalCheck</c> ile kaydedilir.
/// </remarks>
public sealed record EvalSuite
{
    /// <summary>Takim kimligi.</summary>
    public Guid Id { get; init; }

    /// <summary>Takimin ait oldugu kiraci.</summary>
    public required string TenantId { get; init; }

    /// <summary>Takim adi. Kiraci icinde benzersizdir.</summary>
    public required string Name { get; init; }

    /// <summary>Kisa aciklama.</summary>
    public string? Description { get; init; }

    /// <summary>Bu takimin olctugu agent'in adi.</summary>
    public required string AgentName { get; init; }

    /// <summary>
    /// Denetim tanimlari. Ornek: <c>[{"kind":"nonEmpty","minLength":10}]</c>.
    /// Bicim icin bkz. <c>docs/18-DEGERLENDIRME.md</c>, bolum 18.2.
    /// </summary>
    public JsonElement Checks { get; init; }

    /// <summary>Olusturulma zamani (UTC).</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Son guncellenme zamani (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}
