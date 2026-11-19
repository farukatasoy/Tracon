using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="AnthropicProviderOptions"/> settings at application startup.
/// </summary>
/// <remarks>
/// <para>
/// Validation is written by hand; <c>ValidateDataAnnotations()</c> relies on
/// reflection and produces <c>IL2026</c>. <c>Tracon.Anthropic</c> must stay AOT
/// compatible.
/// </para>
/// <para>
/// <strong>Failure messages never contain the API key.</strong> Validation
/// messages go to the log and the startup exception; leaking the key there would
/// expose it.
/// </para>
/// </remarks>
internal sealed class AnthropicProviderOptionsValidator(IConfiguration? configuration = null) : IValidateOptions<AnthropicProviderOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AnthropicProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        // A key the host names a configuration key for is read when a call is made; the host still has to start with
        // one, so a missing value stops it here as it does for a literal.
        var nameGivesKey = SetupConfigurationName.GivesUsableKey(
            configuration,
            options.ApiKeyConfigurationName,
            AnthropicChatClientFactory.ApiKeyOptionName);

        OptionText.AddWhenOnlyWhiteSpace(options.ApiKeyConfigurationName, nameof(AnthropicProviderOptions), nameof(AnthropicProviderOptions.ApiKeyConfigurationName), ref failures);
        OptionText.AddWhenOnlyWhiteSpace(options.EndpointConfigurationName, nameof(AnthropicProviderOptions), nameof(AnthropicProviderOptions.EndpointConfigurationName), ref failures);

        if (string.IsNullOrWhiteSpace(options.ApiKey) && !nameGivesKey)
        {
            (failures ??= []).Add(
                $"{nameof(AnthropicProviderOptions)}.{nameof(AnthropicProviderOptions.ApiKey)} cannot be empty. " +
                "Provide the key through the `UseAnthropic(apiKey)` call, or define " +
                $"'{AnthropicProviderOptions.SectionName}:{nameof(AnthropicProviderOptions.ApiKey)}' " +
                "inside `dotnet user-secrets`." +
                SetupConfigurationName.Hint(options.ApiKeyConfigurationName));
        }
        else if (OutboundApiKey.Refusal(AnthropicChatClientFactory.ApiKeyOptionName, options.ApiKey) is { } refusal)
        {
            (failures ??= []).Add(refusal);
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

        // The request timeout reaches the SDK's HTTP stack and the health
        // check's CancellationTokenSource; past int.MaxValue ms one refuses it at the
        // first request and another wraps it into a negative value.
        if (options.Timeout is { } timeout && !TimerPeriod.IsValidHttpTimeout(timeout))
        {
            (failures ??= []).Add(TimerPeriod.HttpTimeoutFailure(
                $"{nameof(AnthropicProviderOptions)}.{nameof(AnthropicProviderOptions.Timeout)}",
                timeout));
        }

        if (options.MaxRetries is { } retries && retries < 0)
        {
            (failures ??= []).Add(
                $"{nameof(AnthropicProviderOptions)}.{nameof(AnthropicProviderOptions.MaxRetries)} cannot be negative. " +
                $"Actual value: {retries}.");
        }

        // A text of blanks is a typing mistake: the host would call a model called "   ". The check is on the value
        // the options hold, so a value that code assigns is refused too.
        OptionText.AddWhenOnlyWhiteSpace(options.DefaultModel, nameof(AnthropicProviderOptions), nameof(AnthropicProviderOptions.DefaultModel), ref failures);

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
