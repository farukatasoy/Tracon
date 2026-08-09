namespace AgentPrism;

/// <summary>
/// Bir <see cref="CanaryPolicy"/>'yi kanarya ve kontrol kolunun guncel sonuclarina
/// karsi degerlendiren SAF karar mantigi.
/// </summary>
/// <remarks>
/// <para>
/// Veritabani veya model cagirmaz — <see cref="Evaluate"/> yalnizca zaten toplanmis
/// <see cref="ExperimentVariantResult"/> verisi uzerinde calisir. Veri toplama
/// <c>CanaryEvaluationService</c>'in isidir; bu ayrim <c>ExperimentAssignmentResolver.SelectVariant</c>
/// ile ayni gerekcedir — testler DB olmadan calisir.
/// </para>
/// <para>
/// <c>public</c>'tir: <c>GET /api/experiments/{name}/canary</c>
/// (<c>AgentPrism.AspNetCore</c>) "son degerlendirme"yi bu metotla CANLI hesaplar —
/// <see cref="ExperimentAssignmentResolver"/>'in <c>public</c> olmasiyla AYNI gerekce.
/// </para>
/// </remarks>
public static class CanaryEvaluator
{
    /// <summary>
    /// Kanarya politikasini kanarya ve kontrol kolunun guncel sonuclarina karsi
    /// degerlendirir.
    /// </summary>
    /// <param name="policy">Kanarya politikasi.</param>
    /// <param name="results">Deneyin TUM kollarinin guncel sonuclari.</param>
    /// <param name="now">Degerlendirme ani (UTC).</param>
    /// <returns>Degerlendirme sonucu.</returns>
    public static CanaryEvaluation Evaluate(CanaryPolicy policy, IReadOnlyList<ExperimentVariantResult> results, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(results);

        var canary = results.FirstOrDefault(result => string.Equals(result.Variant, policy.CanaryVariant, StringComparison.Ordinal));
        var control = results.FirstOrDefault(result => !string.Equals(result.Variant, policy.CanaryVariant, StringComparison.Ordinal));

        var canarySettled = SettledRuns(canary);
        var controlSettled = SettledRuns(control);

        if (canary is null || control is null || canarySettled < policy.MinSampleSize || controlSettled < policy.MinSampleSize)
        {
            return Build(
                CanaryDecisionKind.InsufficientData,
                $"Asgari sonuclanmis calistirma sayisina ({policy.MinSampleSize}) ulasilmadi: " +
                $"kanarya {canarySettled}, kontrol {controlSettled}.",
                canary,
                control,
                now);
        }

        if (policy.MaxErrorRateDelta is { } maxDelta && canary.ErrorRate is { } canaryRate && control.ErrorRate is { } controlRate)
        {
            var delta = canaryRate - controlRate;

            if (delta > maxDelta)
            {
                return Build(
                    CanaryDecisionKind.RollBack,
                    $"Kanarya hata orani ({canaryRate:P1}) kontrolden ({controlRate:P1}) {maxDelta:P1} esiginden fazla yuksek.",
                    canary,
                    control,
                    now);
            }
        }

        if (policy.MinScore is { } minScore && canary.AverageScore is { } averageScore && averageScore < minScore)
        {
            return Build(
                CanaryDecisionKind.RollBack,
                $"Kanarya ortalama puani ({averageScore:F1}) {minScore} esiginin altinda.",
                canary,
                control,
                now);
        }

        return Build(CanaryDecisionKind.Healthy, "Kanarya kontrolden esik kadar kotu degil.", canary, control, now);
    }

    private static long SettledRuns(ExperimentVariantResult? result)
        => result is null ? 0 : result.CompletedRuns + result.FailedRuns + result.CanceledRuns;

    private static CanaryEvaluation Build(
        CanaryDecisionKind decision,
        string reason,
        ExperimentVariantResult? canary,
        ExperimentVariantResult? control,
        DateTimeOffset now)
        => new()
        {
            Decision = decision,
            Reason = reason,
            Canary = canary,
            Control = control,
            EvaluatedAt = now,
        };
}
