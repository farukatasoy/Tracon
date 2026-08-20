using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Validates <see cref="GoogleProviderOptions"/> settings at application startup.
/// </summary>
/// <remarks>
/// <para>
/// Validation is written by hand; <c>ValidateDataAnnotations()</c> relies on
/// reflection and produces <c>IL2026</c>. <c>AgentPrism.Google</c> must stay AOT
/// compatible.
/// </para>
/// <para>
/// <strong>Error messages never include the API key.</strong>
/// </para>
/// </remarks>
public sealed class GoogleProviderOptionsValidator : IValidateOptions<GoogleProviderOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, GoogleProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            (failures ??= []).Add(
                $"{nameof(GoogleProviderOptions)}.{nameof(GoogleProviderOptions.ApiKey)} cannot be empty. " +
                "Pass the key in the `UseGoogle(apiKey)` call, or " +
                $"set '{GoogleProviderOptions.SectionName}:{nameof(GoogleProviderOptions.ApiKey)}' " +
                "inside `dotnet user-secrets`.");
        }

        if (options.Endpoint is { IsAbsoluteUri: false })
        {
            (failures ??= []).Add(
                $"{nameof(GoogleProviderOptions)}.{nameof(GoogleProviderOptions.Endpoint)} must be an absolute address. " +
                $"Actual value: '{options.Endpoint}'.");
        }

        if (options.Timeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(GoogleProviderOptions)}.{nameof(GoogleProviderOptions.Timeout)} must be greater than zero. " +
                $"Actual value: {timeout}.");
        }

        for (var index = 0; index < options.Models.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(options.Models[index]?.Name))
            {
                (failures ??= []).Add(
                    $"{nameof(GoogleProviderOptions)}.{nameof(GoogleProviderOptions.Models)}[{index}] " +
                    "model name cannot be empty.");
            }
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
