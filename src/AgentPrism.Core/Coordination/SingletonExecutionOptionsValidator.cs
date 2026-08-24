using Microsoft.Extensions.Options;

namespace AgentPrism;

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

        return ValidateOptionsResult.Success;
    }
}
