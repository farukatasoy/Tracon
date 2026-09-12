using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="SingletonExecutionOptions"/> when the application starts.
/// </summary>
/// <remarks>
/// This validation is handwritten. <c>ValidateDataAnnotations()</c> uses reflection
/// and produces <c>IL2026</c>.
/// </remarks>
internal sealed class SingletonExecutionOptionsValidator : IValidateOptions<SingletonExecutionOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, SingletonExecutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.LeaseDuration <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(SingletonExecutionOptions)}.{nameof(SingletonExecutionOptions.LeaseDuration)} " +
                $"must be greater than zero. Actual value: {options.LeaseDuration}.");
        }

        // 🚨 Renewal happens at one third of the lease but never faster than
        // SingletonGuard.MinimumRenewInterval. Below the minimum the renewal
        // lands ON or AFTER the expiry: the lease looks expired while its owner
        // is alive, a second instance takes it over, and the work that had to
        // run on ONE instance starts alternating between them.
        // Only the running hazard is gated on Enabled - while selection is off,
        // the renewal loop never starts. The zero check above stays
        // unconditional: that value is nonsense either way.
        if (options.Enabled && options.LeaseDuration < SingletonGuard.MinimumLeaseDuration)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(SingletonExecutionOptions)}.{nameof(SingletonExecutionOptions.LeaseDuration)} " +
                $"must be at least {SingletonGuard.MinimumLeaseDuration} while " +
                $"{nameof(SingletonExecutionOptions.Enabled)} is true: renewal runs at one third of it and " +
                $"never faster than {SingletonGuard.MinimumRenewInterval}, so a shorter lease would expire " +
                $"under a live owner and another instance would take it over. " +
                $"Actual value: {options.LeaseDuration}.");
        }

        return ValidateOptionsResult.Success;
    }
}
