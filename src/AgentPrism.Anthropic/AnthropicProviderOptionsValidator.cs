using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Validates <see cref="AnthropicProviderOptions"/> settings at application startup.
/// </summary>
/// <remarks>
/// <para>
/// Validation is written by hand; <c>ValidateDataAnnotations()</c> relies on
/// reflection and produces <c>IL2026</c>. <c>AgentPrism.Anthropic</c> must stay AOT
/// compatible. Rationale: <c>docs/KARARLAR.md</c>, decision K-006.
/// </para>
/// <para>
/// <strong>Failure messages never contain the API key.</strong> Validation
/// messages go to the log and the startup exception; leaking the key there would
/// expose it.
/// </para>
/// </remarks>
public sealed class AnthropicProviderOptionsValidator : IValidateOptions<AnthropicProviderOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AnthropicProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            (failures ??= []).Add(
                $"{nameof(AnthropicProviderOptions)}.{nameof(AnthropicProviderOptions.ApiKey)} cannot be empty. " +
                "Provide the key through the `UseAnthropic(apiKey)` call, or define " +
                $"'{AnthropicProviderOptions.SectionName}:{nameof(AnthropicProviderOptions.ApiKey)}' " +
                "inside `dotnet user-secrets`.");
        }

        if (options.Endpoint is { IsAbsoluteUri: false })
        {
            (failures ??= []).Add(
                $"{nameof(AnthropicProviderOptions)}.{nameof(AnthropicProviderOptions.Endpoint)} must be an absolute address. " +
                $"Actual value: '{options.Endpoint}'.");
        }

        if (options.DefaultMaxOutputTokens <= 0)
        {
            (failures ??= []).Add(
                $"{nameof(AnthropicProviderOptions)}.{nameof(AnthropicProviderOptions.DefaultMaxOutputTokens)} " +
                $"must be greater than zero. The Anthropic Messages API requires the `max_tokens` field. " +
                $"Actual value: {options.DefaultMaxOutputTokens}.");
        }

        if (options.Timeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(AnthropicProviderOptions)}.{nameof(AnthropicProviderOptions.Timeout)} must be greater than zero. " +
                $"Actual value: {timeout}.");
        }

        if (options.MaxRetries is { } retries && retries < 0)
        {
            (failures ??= []).Add(
                $"{nameof(AnthropicProviderOptions)}.{nameof(AnthropicProviderOptions.MaxRetries)} cannot be negative. " +
                $"Actual value: {retries}.");
        }

        for (var index = 0; index < options.Models.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(options.Models[index]?.Name))
            {
                (failures ??= []).Add(
                    $"{nameof(AnthropicProviderOptions)}.{nameof(AnthropicProviderOptions.Models)}[{index}] " +
                    "model name cannot be empty.");
            }
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
