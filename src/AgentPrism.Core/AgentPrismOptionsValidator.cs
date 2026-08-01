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

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
