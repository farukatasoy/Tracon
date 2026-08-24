using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Validates <see cref="AgentPrismWebhookOptions"/> when the application starts.
/// </summary>
/// <remarks>
/// This validation is handwritten. <c>ValidateDataAnnotations()</c> uses reflection
/// and produces <c>IL2026</c>.
/// </remarks>
internal sealed class AgentPrismWebhookOptionsValidator : IValidateOptions<AgentPrismWebhookOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AgentPrismWebhookOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (options.Timeout <= TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismWebhookOptions)}.{nameof(AgentPrismWebhookOptions.Timeout)} " +
                $"must be greater than zero. Actual value: {options.Timeout}.");
        }

        if (options.MaxResponseBytes < 1)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismWebhookOptions)}.{nameof(AgentPrismWebhookOptions.MaxResponseBytes)} " +
                $"must be at least 1. Actual value: {options.MaxResponseBytes}.");
        }

        if (options.DisableAfterConsecutiveFailures < 1)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismWebhookOptions)}.{nameof(AgentPrismWebhookOptions.DisableAfterConsecutiveFailures)} " +
                $"must be at least 1. Actual value: {options.DisableAfterConsecutiveFailures}.");
        }

        // The ladder length is also the maximum attempt count. An empty list would
        // mean "do not make any attempt".
        if (options.RetryDelays.Count == 0)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismWebhookOptions)}.{nameof(AgentPrismWebhookOptions.RetryDelays)} " +
                "must contain at least one delay.");
        }

        foreach (var delay in options.RetryDelays)
        {
            if (delay <= TimeSpan.Zero)
            {
                (failures ??= []).Add(
                    $"{nameof(AgentPrismWebhookOptions)}.{nameof(AgentPrismWebhookOptions.RetryDelays)} " +
                $"values must be greater than zero. Actual value: {delay}.");
            }
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
