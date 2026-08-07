using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// <see cref="SingletonExecutionOptions"/> ayarlarini uygulama baslarken dogrular.
/// </summary>
/// <remarks>
/// Dogrulama elle yazilmistir; <c>ValidateDataAnnotations()</c> yansimaya dayanir ve
/// <c>IL2026</c> uretir. Gerekce: <c>docs/KARARLAR.md</c>, karar K-006.
/// </remarks>
public sealed class SingletonExecutionOptionsValidator : IValidateOptions<SingletonExecutionOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, SingletonExecutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.LeaseDuration <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(SingletonExecutionOptions)}.{nameof(SingletonExecutionOptions.LeaseDuration)} " +
                $"sifirdan buyuk olmalidir. Gelen deger: {options.LeaseDuration}.");
        }

        return ValidateOptionsResult.Success;
    }
}
