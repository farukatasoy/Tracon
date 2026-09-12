using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Validates <see cref="TraconDrainOptions"/>.</summary>
internal sealed class TraconDrainOptionsValidator : IValidateOptions<TraconDrainOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TraconDrainOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Timeout <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(TraconDrainOptions)}.{nameof(TraconDrainOptions.Timeout)} " +
                $"must be greater than zero. Actual value: {options.Timeout}.");
        }

        return ValidateOptionsResult.Success;
    }
}
