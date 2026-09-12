using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="AzureOpenAIProviderOptions"/> when the application starts.
/// </summary>
/// <remarks>
/// <para>
/// Validation is written by hand; <c>ValidateDataAnnotations()</c> relies on
/// reflection and produces <c>IL2026</c>. <c>Tracon.Azure</c> must stay AOT
/// compatible.
/// </para>
/// <para>
/// <strong>Error messages contain neither the API key nor the endpoint
/// address.</strong> Validation messages go to the log and to the startup
/// exception.
/// </para>
/// </remarks>
internal sealed class AzureOpenAIProviderOptionsValidator : IValidateOptions<AzureOpenAIProviderOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AzureOpenAIProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (options.Endpoint is null)
        {
            (failures ??= []).Add(
                $"{nameof(AzureOpenAIProviderOptions)}.{nameof(AzureOpenAIProviderOptions.Endpoint)} cannot be empty. " +
                "Azure OpenAI has no single global address; each resource has its own address. " +
                $"Give the '{AzureOpenAIProviderOptions.SectionName}:{nameof(AzureOpenAIProviderOptions.Endpoint)}' " +
                "option in the form 'https://<resource-name>.openai.azure.com/'.");
        }
        else if (!options.Endpoint.IsAbsoluteUri)
        {
            // The address value itself is NOT written into the message: an
            // organization's resource name exposes a topology, and validation
            // messages go to the log.
            (failures ??= []).Add(
                $"{nameof(AzureOpenAIProviderOptions)}.{nameof(AzureOpenAIProviderOptions.Endpoint)} " +
                "must be an absolute address.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey) && options.CredentialFactory is null)
        {
            (failures ??= []).Add(
                $"{nameof(AzureOpenAIProviderOptions)}.{nameof(AzureOpenAIProviderOptions.ApiKey)} or " +
                $"{nameof(AzureOpenAIProviderOptions)}.{nameof(AzureOpenAIProviderOptions.CredentialFactory)} " +
                "must be filled in. Give the key in the `UseAzureOpenAI(endpoint, apiKey)` call, " +
                $"define the '{AzureOpenAIProviderOptions.SectionName}:{nameof(AzureOpenAIProviderOptions.ApiKey)}' " +
                "option inside `dotnet user-secrets`, or give a " +
                $"{nameof(AzureOpenAIProviderOptions.CredentialFactory)} for a managed credential.");
        }

        if (options.Timeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(AzureOpenAIProviderOptions)}.{nameof(AzureOpenAIProviderOptions.Timeout)} must be greater than zero. " +
                $"Actual value: {timeout}.");
        }

        for (var index = 0; index < options.Models.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(options.Models[index]?.Name))
            {
                (failures ??= []).Add(
                    $"{nameof(AzureOpenAIProviderOptions)}.{nameof(AzureOpenAIProviderOptions.Models)}[{index}] " +
                    "deployment name cannot be empty.");
            }
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
