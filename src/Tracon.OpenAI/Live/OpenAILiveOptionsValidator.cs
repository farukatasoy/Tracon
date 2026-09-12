using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Validates <see cref="OpenAILiveOptions"/> while the application starts.</summary>
/// <remarks>
/// <para>
/// The validation is written by hand; <c>ValidateDataAnnotations()</c> relies on
/// reflection and produces <c>IL2026</c>. <c>Tracon.OpenAI</c> must stay AOT
/// compatible.
/// </para>
/// <para>
/// <strong>The failure messages never contain the API key.</strong> They go to the
/// log and to the startup exception.
/// </para>
/// </remarks>
internal sealed class OpenAILiveOptionsValidator : IValidateOptions<OpenAILiveOptions>
{
    private readonly IOptionsMonitor<OpenAIProviderOptions> _providerOptions;

    /// <summary>Creates a validator.</summary>
    /// <param name="providerOptions">The OpenAI provider options, which carry the API key.</param>
    public OpenAILiveOptionsValidator(IOptionsMonitor<OpenAIProviderOptions> providerOptions)
    {
        ArgumentNullException.ThrowIfNull(providerOptions);

        _providerOptions = providerOptions;
    }

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OpenAILiveOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            (failures ??= []).Add(
                $"{nameof(OpenAILiveOptions)}.{nameof(OpenAILiveOptions.Model)} cannot be empty.");
        }

        if (options.Endpoint is { IsAbsoluteUri: false })
        {
            (failures ??= []).Add(
                $"{nameof(OpenAILiveOptions)}.{nameof(OpenAILiveOptions.Endpoint)} must be an absolute address. " +
                $"Received value: '{options.Endpoint}'.");
        }

        if (options.MaxAppendCharacters <= 0)
        {
            (failures ??= []).Add(
                $"{nameof(OpenAILiveOptions)}.{nameof(OpenAILiveOptions.MaxAppendCharacters)} must be greater than zero. " +
                $"Received value: {options.MaxAppendCharacters}.");
        }

        if (options.Timeout <= TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(OpenAILiveOptions)}.{nameof(OpenAILiveOptions.Timeout)} must be greater than zero. " +
                $"Received value: {options.Timeout}.");
        }

        // 🚨 The message says WHICH CALL is missing. The live provider reuses the
        // OpenAI key, so an application that calls UseOpenAILive() without UseOpenAI()
        // would otherwise fail at the first session with an opaque authentication error.
        if (string.IsNullOrWhiteSpace(_providerOptions.CurrentValue.ApiKey))
        {
            (failures ??= []).Add(
                "The OpenAI live voice provider reuses the OpenAI API key, and none is configured. " +
                "Call `UseOpenAI(...)` before `UseOpenAILive(...)`, or set " +
                $"'{OpenAIProviderOptions.SectionName}:{nameof(OpenAIProviderOptions.ApiKey)}'.");
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
