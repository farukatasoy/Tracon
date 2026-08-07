namespace AgentPrism;

/// <summary>
/// Ham bir calistirma hatasini bir sinifa ve kumeleme parmak izine cevirir.
/// </summary>
/// <remarks>
/// <para>
/// Yalnizca <strong>hata</strong> yolunda cagrilir; basarili bir calistirmada
/// hic tetiklenmez ve sicak yolda tahsis uretmemelidir.
/// </para>
/// <para>
/// AgentPrism'in taksonomisi kendi gorusudur; bir tuketici kendi sinifini veya
/// kumeleme kuralini isteyebilir. <c>TryAddSingleton</c> ile kaydedilir,
/// tuketicinin kaydi kazanir (K4).
/// </para>
/// </remarks>
public interface IRunErrorClassifier
{
    /// <summary>
    /// Hatayi siniflandirir. Hicbir kurala uymuyorsa
    /// <see cref="RunErrorClass.Unknown"/> doner — <strong>tahmin etmez</strong>.
    /// </summary>
    /// <param name="runError">Siniflandirilacak ham hata.</param>
    /// <returns>Sinif ve kumeleme parmak izi.</returns>
    RunErrorClassification Classify(RunError runError);
}
