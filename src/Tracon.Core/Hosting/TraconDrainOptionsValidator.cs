using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Validates <see cref="TraconDrainOptions"/>.</summary>
internal sealed class TraconDrainOptionsValidator : IValidateOptions<TraconDrainOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TraconDrainOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // F-280: the drain arms this timeout at shutdown; an out-of-range value threw
        // there and the drain was skipped.
        if (!TimerPeriod.IsValid(options.Timeout))
        {
            return ValidateOptionsResult.Fail(TimerPeriod.Failure(
                $"{nameof(TraconDrainOptions)}.{nameof(TraconDrainOptions.Timeout)}",
                options.Timeout));
        }

        return ValidateOptionsResult.Success;
    }
}
