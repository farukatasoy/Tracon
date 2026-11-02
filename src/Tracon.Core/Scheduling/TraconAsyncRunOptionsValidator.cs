using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="TraconAsyncRunOptions"/> when the application starts.
/// </summary>
/// <remarks>
/// A queued run with fewer than one attempt is failed by the worker without running, so every
/// <c>Prefer: respond-async</c> request is accepted and then lost. While the feature is off the value is not read.
/// </remarks>
internal sealed class TraconAsyncRunOptionsValidator : IValidateOptions<TraconAsyncRunOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TraconAsyncRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.Enabled && options.MaxAttempts < 1
            ? ValidateOptionsResult.Fail(
                $"{nameof(TraconAsyncRunOptions)}.{nameof(TraconAsyncRunOptions.MaxAttempts)} " +
                $"must be at least 1 while {nameof(TraconAsyncRunOptions.Enabled)} is on. Actual value: {options.MaxAttempts}.")
            : ValidateOptionsResult.Success;
    }
}
