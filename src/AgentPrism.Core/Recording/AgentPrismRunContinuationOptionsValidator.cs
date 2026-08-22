using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Validates <see cref="AgentPrismRunContinuationOptions"/>.</summary>
public sealed class AgentPrismRunContinuationOptionsValidator : IValidateOptions<AgentPrismRunContinuationOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AgentPrismRunContinuationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxAttempts < 1)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(AgentPrismRunContinuationOptions)}.{nameof(AgentPrismRunContinuationOptions.MaxAttempts)} " +
                $"must be at least 1. Actual value: {options.MaxAttempts}.");
        }

        return ValidateOptionsResult.Success;
    }
}
