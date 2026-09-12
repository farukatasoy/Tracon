using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="TraconStructuredResponseOptions"/> when the application starts.
/// </summary>
/// <remarks>
/// This validation is handwritten. <c>ValidateDataAnnotations()</c> uses reflection
/// and produces <c>IL2026</c>.
/// </remarks>
internal sealed class TraconStructuredResponseOptionsValidator : IValidateOptions<TraconStructuredResponseOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TraconStructuredResponseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxRepairAttempts < 0)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(TraconStructuredResponseOptions)}.{nameof(TraconStructuredResponseOptions.MaxRepairAttempts)} " +
                $"must not be negative. Actual value: {options.MaxRepairAttempts}.");
        }

        return ValidateOptionsResult.Success;
    }
}
