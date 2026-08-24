using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Validates <see cref="AgentPrismDrainOptions"/>.</summary>
internal sealed class AgentPrismDrainOptionsValidator : IValidateOptions<AgentPrismDrainOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AgentPrismDrainOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Timeout <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(AgentPrismDrainOptions)}.{nameof(AgentPrismDrainOptions.Timeout)} " +
                $"must be greater than zero. Actual value: {options.Timeout}.");
        }

        return ValidateOptionsResult.Success;
    }
}
