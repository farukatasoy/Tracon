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
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
