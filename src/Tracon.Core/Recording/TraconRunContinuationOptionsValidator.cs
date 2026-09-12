using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Validates <see cref="TraconRunContinuationOptions"/>.</summary>
internal sealed class TraconRunContinuationOptionsValidator : IValidateOptions<TraconRunContinuationOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TraconRunContinuationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxAttempts < 1)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(TraconRunContinuationOptions)}.{nameof(TraconRunContinuationOptions.MaxAttempts)} " +
                $"must be at least 1. Actual value: {options.MaxAttempts}.");
        }

        return ValidateOptionsResult.Success;
    }
}
