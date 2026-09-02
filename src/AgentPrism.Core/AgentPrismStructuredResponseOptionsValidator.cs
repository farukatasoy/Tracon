using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Validates <see cref="AgentPrismStructuredResponseOptions"/> when the application starts.
/// </summary>
/// <remarks>
/// This validation is handwritten. <c>ValidateDataAnnotations()</c> uses reflection
/// and produces <c>IL2026</c>.
/// </remarks>
internal sealed class AgentPrismStructuredResponseOptionsValidator : IValidateOptions<AgentPrismStructuredResponseOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AgentPrismStructuredResponseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxRepairAttempts < 0)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(AgentPrismStructuredResponseOptions)}.{nameof(AgentPrismStructuredResponseOptions.MaxRepairAttempts)} " +
                $"must not be negative. Actual value: {options.MaxRepairAttempts}.");
        }

        return ValidateOptionsResult.Success;
    }
}
