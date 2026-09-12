using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="OpenAIProviderOptions"/> while the application starts.
/// </summary>
/// <remarks>
/// <para>
/// The validation is written by hand; <c>ValidateDataAnnotations()</c> relies on
/// reflection and produces <c>IL2026</c>. <c>Tracon.OpenAI</c> must stay AOT
/// compatible.
/// </para>
/// <para>
/// <strong>The failure messages never contain the API key.</strong> Validation messages
/// go to the log and to the startup exception; leaking the key there exposes it.
/// </para>
/// <para>
/// This validator checks both the unnamed (default) options instance of
/// <c>UseOpenAI()</c> and the named instances of <c>UseOpenAICompatible()</c>. The
/// <c>name</c> parameter of <see cref="Validate(string?, OpenAIProviderOptions)"/> tells
/// the two apart: on the unnamed instance (<c>name</c> is empty) the API key is required,
/// because the official OpenAI service does not work without a key. On a named instance
/// (a compatible provider) the API key is <strong>optional</strong> (local servers do not
/// ask for one) but <see cref="OpenAIProviderOptions.Endpoint"/> is required — if it were
/// left empty, the request would silently go to the official OpenAI address.
/// </para>
/// </remarks>
internal sealed class OpenAIProviderOptionsValidator : IValidateOptions<OpenAIProviderOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OpenAIProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var isDefaultInstance = string.IsNullOrEmpty(name);
        List<string>? failures = null;

        if (isDefaultInstance && string.IsNullOrWhiteSpace(options.ApiKey))
        {
            (failures ??= []).Add(
                $"{nameof(OpenAIProviderOptions)}.{nameof(OpenAIProviderOptions.ApiKey)} cannot be empty. " +
                "Pass the key to the `UseOpenAI(apiKey)` call, or define " +
                $"'{OpenAIProviderOptions.SectionName}:{nameof(OpenAIProviderOptions.ApiKey)}' " +
                "in `dotnet user-secrets`.");
        }

        if (!isDefaultInstance && options.Endpoint is null)
        {
            (failures ??= []).Add(
                $"{nameof(OpenAIProviderOptions)}.{nameof(OpenAIProviderOptions.Endpoint)} is required for " +
                "compatible providers. If it were left empty, the request would silently go to the " +
                "official OpenAI address. Set it with `UseOpenAICompatible(name, o => o.Endpoint = new Uri(\"https://...\"))`.");
        }

        if (options.Endpoint is { IsAbsoluteUri: false })
        {
            (failures ??= []).Add(
                $"{nameof(OpenAIProviderOptions)}.{nameof(OpenAIProviderOptions.Endpoint)} must be an absolute address. " +
                $"Received value: '{options.Endpoint}'.");
        }

        if (options.Timeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(OpenAIProviderOptions)}.{nameof(OpenAIProviderOptions.Timeout)} must be greater than zero. " +
                $"Received value: {timeout}.");
        }

        for (var index = 0; index < options.Models.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(options.Models[index]?.Name))
            {
                (failures ??= []).Add(
                    $"The model name for {nameof(OpenAIProviderOptions)}.{nameof(OpenAIProviderOptions.Models)}[{index}] " +
                    "cannot be empty.");
            }
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
