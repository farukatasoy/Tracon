using System.Text.Json;

namespace AgentPrism;

/// <summary>Bir <see cref="EvalRun"/> icinde tek bir <see cref="EvalCase"/>'in sonucu.</summary>
/// <remarks>
/// <see cref="CaseId"/> kasitli olarak bir yabanci anahtar tasimaz: bir vaka
/// sonradan degistirilse veya silinse bile gecmis sonuc kaydi anlasilir kalir
/// (append-only ruh, K-014 ile ayni gerekce).
/// </remarks>
public sealed record EvalCaseResult
{
    /// <summary>Sonuc kaydinin kimligi.</summary>
    public Guid Id { get; init; }

    /// <summary>Ait oldugu kosunun kimligi.</summary>
    public required Guid EvalRunId { get; init; }

    /// <summary>Olculen vakanin kimligi.</summary>
    public required Guid CaseId { get; init; }

    /// <summary>
    /// Bu vakayi islerken olusan calistirma kaydinin kimligi. Boylece bir eval
    /// hatasi tek tikla transkripte ve span agacina gider.
    /// </summary>
    public Guid? RunId { get; init; }

    /// <summary>Vakanin tum denetimleri gectigini bildirir.</summary>
    public required bool Passed { get; init; }

    /// <summary>Agent'in urettigi metin cikti.</summary>
    public string? Output { get; init; }

    /// <summary>Denetim bazinda skor listesi (serbest JSON).</summary>
    public JsonElement Scores { get; init; }

    /// <summary>Basarisizlik nedeni. Yalnizca <see cref="Passed"/> <see langword="false"/> ise dolu.</summary>
    public string? FailureReason { get; init; }
}
