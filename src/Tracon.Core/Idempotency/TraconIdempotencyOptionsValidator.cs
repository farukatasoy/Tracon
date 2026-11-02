using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="TraconIdempotencyOptions"/> when the application starts.
/// </summary>
/// <remarks>
/// A key length below one refuses every <c>Idempotency-Key</c> with <c>400</c>, which looks like a
/// client error and not like the setting that causes it. While the feature is off the value is not read.
/// </remarks>
internal sealed class TraconIdempotencyOptionsValidator : IValidateOptions<TraconIdempotencyOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TraconIdempotencyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.Enabled && options.MaxKeyLength < 1
            ? ValidateOptionsResult.Fail(
                $"{nameof(TraconIdempotencyOptions)}.{nameof(TraconIdempotencyOptions.MaxKeyLength)} " +
                $"must be at least 1 while {nameof(TraconIdempotencyOptions.Enabled)} is on. Actual value: {options.MaxKeyLength}.")
            : ValidateOptionsResult.Success;
    }
}
