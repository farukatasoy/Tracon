using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// <see cref="AgentPrismOptions"/> ayarlarini uygulama baslarken dogrular.
/// </summary>
/// <remarks>
/// Dogrulama elle yazilmistir; <c>ValidateDataAnnotations()</c> kullanilmaz.
/// Sebep: DataAnnotations dogrulamasi yansimaya dayanir ve <c>IL2026</c> uretir.
/// <c>AgentPrism.Core</c> AOT uyumlu kalmalidir.
/// Gerekce: <c>docs/KARARLAR.md</c>, karar K-006.
/// </remarks>
public sealed class AgentPrismOptionsValidator : IValidateOptions<AgentPrismOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AgentPrismOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (string.IsNullOrWhiteSpace(options.DefaultTenantId))
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismOptions)}.{nameof(AgentPrismOptions.DefaultTenantId)} bos olamaz.");
        }

        var recording = options.RunRecording;

        if (recording is null)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismOptions)}.{nameof(AgentPrismOptions.RunRecording)} bos olamaz.");
        }
        else if (recording.MaxPayloadLength is < 0 or > 1_048_576)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismRunRecordingOptions)}.{nameof(AgentPrismRunRecordingOptions.MaxPayloadLength)} " +
                $"0 ile 1048576 arasinda olmalidir. Gelen deger: {recording.MaxPayloadLength}.");
        }

        var circuitBreaker = options.CircuitBreaker;

        if (circuitBreaker is null)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismOptions)}.{nameof(AgentPrismOptions.CircuitBreaker)} bos olamaz.");
        }
        else
        {
            if (circuitBreaker.FailureThreshold < 1)
            {
                (failures ??= []).Add(
                    $"{nameof(AgentPrismCircuitBreakerOptions)}.{nameof(AgentPrismCircuitBreakerOptions.FailureThreshold)} " +
                    $"en az 1 olmalidir. Gelen deger: {circuitBreaker.FailureThreshold}.");
            }

            if (circuitBreaker.BreakDuration <= TimeSpan.Zero)
            {
                (failures ??= []).Add(
                    $"{nameof(AgentPrismCircuitBreakerOptions)}.{nameof(AgentPrismCircuitBreakerOptions.BreakDuration)} " +
                    $"sifirdan buyuk olmalidir. Gelen deger: {circuitBreaker.BreakDuration}.");
            }
        }

        var health = options.Health;

        if (health is null)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismOptions)}.{nameof(AgentPrismOptions.Health)} bos olamaz.");
        }
        else
        {
            if (health.CacheTtl <= TimeSpan.Zero)
            {
                (failures ??= []).Add(
                    $"{nameof(AgentPrismHealthOptions)}.{nameof(AgentPrismHealthOptions.CacheTtl)} " +
                    $"sifirdan buyuk olmalidir. Gelen deger: {health.CacheTtl}.");
            }

            if (health.BackgroundInterval is { } interval && interval <= TimeSpan.Zero)
            {
                (failures ??= []).Add(
                    $"{nameof(AgentPrismHealthOptions)}.{nameof(AgentPrismHealthOptions.BackgroundInterval)} " +
                    $"verilmisse sifirdan buyuk olmalidir. Gelen deger: {interval}.");
            }
        }

        var skills = options.Skills;

        if (skills is null)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismOptions)}.{nameof(AgentPrismOptions.Skills)} bos olamaz.");
        }
        else
        {
            if (skills.MaxSkillsPerAgent < 1)
            {
                (failures ??= []).Add(
                    $"{nameof(AgentPrismSkillOptions)}.{nameof(AgentPrismSkillOptions.MaxSkillsPerAgent)} " +
                    $"en az 1 olmalidir. Gelen deger: {skills.MaxSkillsPerAgent}.");
            }

            if (skills.MaxInstructionsLength < 1 || skills.MaxResourceContentLength < 1 || skills.MaxResourcesPerSkill < 1)
            {
                (failures ??= []).Add(
                    $"{nameof(AgentPrismSkillOptions)} sinirlari sifirdan buyuk olmalidir.");
            }

            ValidateScripts(skills.Scripts, ref failures);
        }

        ValidatePricing(options.Pricing, ref failures);

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>Script calistirma ayarlarini dogrular.</summary>
    /// <remarks>
    /// Bu dogrulama bir guvenlik kapisidir: eksik yapilandirmayla acilmis bir
    /// script calistirma ozelligi, calisma aninda degil <strong>acilista</strong>
    /// hata vermelidir.
    /// </remarks>
    private static void ValidateScripts(AgentPrismSkillScriptOptions? scripts, ref List<string>? failures)
    {
        if (scripts is null)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSkillOptions)}.{nameof(AgentPrismSkillOptions.Scripts)} bos olamaz.");
            return;
        }

        if (!scripts.Enabled)
        {
            return;
        }

        if (!scripts.PlatformIsolationAcknowledged)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSkillScriptOptions)}.{nameof(AgentPrismSkillScriptOptions.Enabled)} acikken " +
                $"{nameof(AgentPrismSkillScriptOptions.PlatformIsolationAcknowledged)} da true olmalidir. " +
                "AgentPrism isletim sistemi duzeyinde yalitim saglamaz: ag erisimi, dosya sistemi, CPU/bellek " +
                "kotasi ve ayricalik dusurme barindirma ortaminin sorumlulugundadir. Script calistirmayi " +
                "yalnizca container icinde, ayricaliksiz bir kullaniciyla ve kisitli ag ile acin.");
        }

        if (scripts.Timeout <= TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSkillScriptOptions)}.{nameof(AgentPrismSkillScriptOptions.Timeout)} " +
                $"sifirdan buyuk olmalidir. Gelen deger: {scripts.Timeout}.");
        }

        if (scripts.MaxOutputBytes < 1 || scripts.MaxArgumentBytes < 1 || scripts.MaxScriptContentLength < 1)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSkillScriptOptions)} boyut sinirlari sifirdan buyuk olmalidir.");
        }

        if (scripts.MaxScriptsPerSkill < 1 || scripts.MaxConcurrentPerTenant < 1 || scripts.MaxConcurrentTotal < 1)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSkillScriptOptions)} adet sinirlari en az 1 olmalidir.");
        }

        if (scripts.MaxConcurrentPerTenant > scripts.MaxConcurrentTotal)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSkillScriptOptions)}.{nameof(AgentPrismSkillScriptOptions.MaxConcurrentPerTenant)} " +
                $"({scripts.MaxConcurrentPerTenant}) toplam sinirdan ({scripts.MaxConcurrentTotal}) buyuk olamaz.");
        }

        if (scripts.SearchDepth < 1)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSkillScriptOptions)}.{nameof(AgentPrismSkillScriptOptions.SearchDepth)} " +
                $"en az 1 olmalidir. Gelen deger: {scripts.SearchDepth}.");
        }

        foreach (var pair in scripts.Interpreters)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
            {
                (failures ??= []).Add(
                    $"{nameof(AgentPrismSkillScriptOptions)}.{nameof(AgentPrismSkillScriptOptions.Interpreters)} " +
                    "icinde bos uzanti veya bos yorumlayici yolu var.");
                break;
            }
        }
    }

    /// <summary>
    /// Yapilandirmadan verilen fiyat gecersiz kilmalarinin negatif olmadigini
    /// dogrular. Negatif bir fiyat maliyet raporunu sessizce bozardi.
    /// </summary>
    private static void ValidatePricing(AgentPrismPricingOptions? pricing, ref List<string>? failures)
    {
        if (pricing is null)
        {
            return;
        }

        foreach (var (providerName, models) in pricing.Providers)
        {
            foreach (var (modelName, price) in models)
            {
                if (price.InputCostPerMillionTokens is < 0 || price.OutputCostPerMillionTokens is < 0)
                {
                    (failures ??= []).Add(
                        $"{nameof(AgentPrismPricingOptions)}: '{providerName}:{modelName}' icin fiyat negatif olamaz.");
                }
            }
        }

        foreach (var (providerName, models) in pricing.Voice)
        {
            foreach (var (modelName, price) in models)
            {
                if (price.PerMillionCharacters is < 0 || price.PerMinute is < 0)
                {
                    (failures ??= []).Add(
                        $"{nameof(AgentPrismPricingOptions)}: 'Voice:{providerName}:{modelName}' icin fiyat negatif olamaz.");
                }
            }
        }
    }
}
