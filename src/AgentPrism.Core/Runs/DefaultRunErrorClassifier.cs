using System.Text.RegularExpressions;

namespace AgentPrism;

/// <summary>
/// AgentPrism'in yerlesik hata siniflandiricisi.
/// </summary>
/// <remarks>
/// <para>
/// Once hatanin <strong>kararli kimligini</strong> (<see cref="RunError.Type"/>)
/// tam eslesmeyle dener; bu, <see cref="AgentPrismException"/> alt tiplerinin
/// yazdigi degeri (ornek: <c>content_filtered</c>) hem de bu deger eklenmeden
/// once yazilmis eski tam tip adini (ornek:
/// <c>AgentPrism.AgentPrismCompilationException</c>) AYNI sinifa esler —
/// gecmis kayitlar bozulmadan yeni taksonomiye katilir.
/// </para>
/// <para>
/// Tam eslesme yoksa mesaj ve tip adi uzerinde desen aramasina duser. Hicbir
/// kurala uymayan hata <see cref="RunErrorClass.Unknown"/> olur —
/// <strong>tahmin edilmez</strong>.
/// </para>
/// <para>
/// Yalniz hata yolunda cagrilir (bkz. <see cref="IRunErrorClassifier"/>); sicak
/// yolda tahsis uretmemek icin desenler kaynak ureteciyle (<c>GeneratedRegex</c>)
/// yazilmistir.
/// </para>
/// </remarks>
public sealed partial class DefaultRunErrorClassifier : IRunErrorClassifier
{
    private static readonly Dictionary<string, RunErrorClass> StableIdentities = new(StringComparer.Ordinal)
    {
        [AgentPrismContentFilteredException.ContentFilteredErrorType] = RunErrorClass.ContentFiltered,
        [AgentPrismCompilationException.CompilationFailedErrorType] = RunErrorClass.CompilationFailed,
        ["AgentPrism.AgentPrismCompilationException"] = RunErrorClass.CompilationFailed,
        [AgentPrismProviderUnavailableException.ProviderUnavailableErrorType] = RunErrorClass.ProviderUnavailable,
        ["AgentPrism.AgentPrismProviderUnavailableException"] = RunErrorClass.ProviderUnavailable,
    };

    /// <inheritdoc />
    public RunErrorClassification Classify(RunError runError)
    {
        ArgumentNullException.ThrowIfNull(runError);

        return new RunErrorClassification
        {
            Class = ClassifyCore(runError),
            Fingerprint = ErrorFingerprint.Compute(runError.Message),
        };
    }

    private static RunErrorClass ClassifyCore(RunError runError)
    {
        if (StableIdentities.TryGetValue(runError.Type, out var stable))
        {
            return stable;
        }

        if (CanceledTypePattern().IsMatch(runError.Type))
        {
            return RunErrorClass.Canceled;
        }

        if (TimeoutPattern().IsMatch(runError.Type) || TimeoutPattern().IsMatch(runError.Message))
        {
            return RunErrorClass.Timeout;
        }

        if (RateLimitPattern().IsMatch(runError.Message) || RateLimitPattern().IsMatch(runError.Type))
        {
            return RunErrorClass.RateLimited;
        }

        if (QuotaPattern().IsMatch(runError.Message))
        {
            return RunErrorClass.QuotaExceeded;
        }

        if (ToolErrorPattern().IsMatch(runError.Message))
        {
            return RunErrorClass.ToolError;
        }

        if (ProviderErrorTypePattern().IsMatch(runError.Type) || ProviderErrorMessagePattern().IsMatch(runError.Message))
        {
            return RunErrorClass.ProviderError;
        }

        return RunErrorClass.Unknown;
    }

    [GeneratedRegex(
        @"(?:^|\.)(?:OperationCanceledException|TaskCanceledException)$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex CanceledTypePattern();

    [GeneratedRegex(
        @"timeoutexception|\btimed?[\s_-]?out\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex TimeoutPattern();

    [GeneratedRegex(
        @"\b429\b|toomanyrequests|\brate[\s_-]?limit(?:ed|ing)?\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex RateLimitPattern();

    [GeneratedRegex(@"\bquota\b|\bkota\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex QuotaPattern();

    [GeneratedRegex(@"\btool\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ToolErrorPattern();

    // 🚨 Olculdu (samples/AgentPrism.Api, gercek bir OpenAI 404 yaniti):
    // resmi saglayici SDK'lari HttpRequestException FIRLATMAZ. OpenAI'in
    // System.ClientModel tabanli istemcisi ClientResultException, Azure
    // SDK'lari RequestFailedException firlatir. Tip deseni bu yuzden SDK
    // sarmalayicilarini da kapsar; System.Net tipleri (soket/IO) yalniz
    // dogrudan HTTP istemcisi kullanan saglayicilar icindir.
    [GeneratedRegex(
        @"httprequestexception|socketexception|ioexception|clientresultexception|requestfailedexception|apiexception",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex ProviderErrorTypePattern();

    // Yedek imza: tip taninmasa bile mesajda "HTTP 4xx"/"HTTP 5xx" gorulmesi
    // saglayici tarafinda bir HTTP hatasi oldugunu gosterir (ornek: "HTTP 404
    // (invalid_request_error: model_not_found)").
    [GeneratedRegex(@"\bHTTP\s+[45]\d{2}\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ProviderErrorMessagePattern();
}
