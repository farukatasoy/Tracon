using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="GoogleProviderOptions"/> settings at application startup.
/// </summary>
/// <remarks>
/// <para>
/// Validation is written by hand; <c>ValidateDataAnnotations()</c> relies on
/// reflection and produces <c>IL2026</c>. <c>Tracon.Google</c> must stay AOT
/// compatible.
/// </para>
/// <para>
/// <strong>Error messages never include the API key.</strong>
/// </para>
/// </remarks>
internal sealed class GoogleProviderOptionsValidator : IValidateOptions<GoogleProviderOptions>
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

        // F-280: the request timeout reaches the SDK's HTTP stack and the health
        // check's CancellationTokenSource; past int.MaxValue ms one refuses it at the
        // first request and another wraps it into a negative value.
        if (options.Timeout is { } timeout && !TimerPeriod.IsValidHttpTimeout(timeout))
        {
            (failures ??= []).Add(TimerPeriod.HttpTimeoutFailure(
                $"{nameof(GoogleProviderOptions)}.{nameof(GoogleProviderOptions.Timeout)}",
                timeout));
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
