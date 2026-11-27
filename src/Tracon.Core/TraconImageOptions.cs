using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Settings for image generation.</summary>
/// <remarks>
/// <para>
/// The <c>generate_image</c> tool and the operator endpoint are off by default. Setting <see cref="Enabled"/> to
/// <see langword="true"/> registers <c>generate_image</c> only when an <see cref="Microsoft.Extensions.AI.IImageGenerator"/>
/// is also registered.
/// </para>
/// <para>
/// A host can register more than one image provider (<c>UseOpenAIImages</c>, <c>UseAzureOpenAIImages</c>,
/// <c>UseGoogleImages</c>). Each provider keeps its own model and settings in <see cref="Providers"/>, and
/// <see cref="Provider"/> names the one the tool and the operator endpoint draw with. A host that registers one provider
/// does not need to name it.
/// </para>
/// </remarks>
public sealed class TraconImageOptions
{
    /// <summary>Default configuration section name.</summary>
    public const string SectionName = "Tracon:Images";

    /// <summary>Gets or sets whether the image tool and operator endpoint are enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the name of the provider the tool and the operator endpoint draw with.</summary>
    /// <remarks>
    /// <para>
    /// Optional when exactly one image provider is registered: that one is the default. Required when more than one is
    /// registered and <see cref="Enabled"/> is <see langword="true"/>, or when the generator is a custom
    /// <see cref="Microsoft.Extensions.AI.IImageGenerator"/> registered without a provider extension. Like every Tracon
    /// option it is read when the host starts: a change needs a restart.
    /// </para>
    /// <para>The name is the stable provider name: <c>openai</c>, <c>azure-openai</c> or <c>google</c>.</para>
    /// </remarks>
    public string? Provider { get; set; }

    /// <summary>Gets the model and the settings of each image provider, by provider name.</summary>
    /// <remarks>
    /// Each <c>Use*Images</c> call writes to the entry of its own provider and no other, so two providers never share a
    /// model. Configuration binds the <c>Tracon:Images:Providers:{name}:Model</c> keys into it.
    /// </remarks>
    public IDictionary<string, TraconImageProviderOptions> Providers { get; } =
        new Dictionary<string, TraconImageProviderOptions>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the most images one call may produce. Default is one.</summary>
    public int MaxImagesPerRequest { get; set; } = 1;

    /// <summary>
    /// Gets or sets the longest one <c>generate_image</c> call may run. Default
    /// is two minutes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The installation-wide <c>Tracon:Tools:DefaultTimeout</c> is 30 seconds,
    /// which suits a tool that reads a database or calls an API. Image
    /// generation does not belong in that class: a measured <c>gpt-image-1</c>
    /// request routinely needs 30 to 35 seconds, so the generic default cut off
    /// a call that was about to succeed and the model was told it had failed.
    /// </para>
    /// <para>
    /// A shorter value is legitimate for an installation that would rather fail
    /// fast. The charge is not lost either way: a call that outlives its
    /// timeout is cancelled, and if the provider finishes anyway the spend is
    /// recorded against the original call
    /// (<see cref="ToolInvocationRecord.LateCompletedAt"/>).
    /// </para>
    /// <para>Must be from one millisecond to about 49.7 days; any other value fails at startup.</para>
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);
}

/// <summary>The model and the settings of one image provider.</summary>
public sealed class TraconImageProviderOptions
{
    /// <summary>Gets or sets the image model, or the Azure deployment.</summary>
    /// <remarks>
    /// Image model names are not inferred from a chat model. Providers use distinct model catalogues, and an inferred name
    /// could spend money on the wrong model.
    /// </remarks>
    public string? Model { get; set; }

    /// <summary>
    /// Gets or sets the most times an image call is sent again after a failure the provider client considers worth repeating.
    /// When <see langword="null"/>, the count of the provider's client applies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An image call is billed, and the client sends it again on <c>429</c>, <c>500</c> and <c>503</c>: with the default of
    /// three, one failing call reaches the server four times. The provider's client shares its retry count with chat
    /// (<c>OpenAIProviderOptions.MaxRetries</c>, three by default; Azure OpenAI has no option and uses three), and a count that
    /// suits chat can be too high for a call that costs money. This value is a ceiling for image calls alone: <c>0</c> sends a
    /// call once, <c>1</c> sends it at most twice. It only lowers: a value above the client's own count changes nothing, and
    /// chat calls on the same client keep the client's count.
    /// </para>
    /// <para>
    /// Must be from <c>0</c> to <c>10</c> when set. Only the OpenAI and Azure OpenAI image providers take it: a value on any
    /// other provider (Google's image client has no retry setting) fails at startup instead of being ignored. Configuration
    /// binds it (<c>Tracon:Images:Providers:{name}:MaxRetries</c>).
    /// </para>
    /// </remarks>
    public int? MaxRetries { get; set; }

    /// <summary>
    /// Gets the provider-specific settings, keyed under the provider's prefix (for example <c>openai.quality</c>). Empty by
    /// default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A key the provider does not support, a key of another provider and a value of the wrong type fail at startup, and a
    /// call that carries one is refused before the provider is called: nothing is ignored silently. The message names the key,
    /// never the value.
    /// </para>
    /// <para>
    /// OpenAI and Azure OpenAI support <c>openai.quality</c> and <c>azure-openai.quality</c> (a string such as
    /// <c>low</c>, <c>medium</c>, <c>high</c> or <c>auto</c>). Google supports none. Code sets them; configuration does not bind
    /// this dictionary.
    /// </para>
    /// <para>
    /// These are the settings of every call to the provider. A host's own call can lay its own over them for that call alone
    /// (<see cref="TraconImageRequest.ProviderSettings"/>).
    /// </para>
    /// </remarks>
    public IDictionary<string, JsonElement> ProviderSettings { get; } =
        new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Validates <see cref="TraconImageOptions"/> during application startup.</summary>
internal sealed class TraconImageOptionsValidator : IValidateOptions<TraconImageOptions>
{
    private readonly IReadOnlyList<ImageProviderRegistration> _registrations;

    /// <summary>Creates a validator.</summary>
    /// <param name="registrations">The image providers the host registered; none when the generator is the host's own.</param>
    public TraconImageOptionsValidator(IEnumerable<ImageProviderRegistration>? registrations = null)
        => _registrations = [.. registrations ?? []];

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TraconImageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (options.MaxImagesPerRequest < 1)
        {
            (failures ??= []).Add(
                $"{nameof(TraconImageOptions)}.{nameof(TraconImageOptions.MaxImagesPerRequest)} must be at least 1.");
        }

        if (!TimerPeriod.IsValid(options.Timeout))
        {
            (failures ??= []).Add(TimerPeriod.Failure(
                $"{nameof(TraconImageOptions)}.{nameof(TraconImageOptions.Timeout)}",
                options.Timeout));
        }

        // Refused while image generation is off as well: a name of blanks is not "not set".
        OptionText.AddWhenOnlyWhiteSpace(options.Provider, nameof(TraconImageOptions), nameof(TraconImageOptions.Provider), ref failures);

        foreach (var (providerName, provider) in options.Providers)
        {
            if (string.IsNullOrWhiteSpace(providerName))
            {
                (failures ??= []).Add($"{nameof(TraconImageOptions)}.{nameof(TraconImageOptions.Providers)} has an entry whose provider name is blank.");

                continue;
            }

            OptionText.AddWhenOnlyWhiteSpace(
                provider.Model,
                $"{nameof(TraconImageOptions)}.{nameof(TraconImageOptions.Providers)}[{providerName}]",
                nameof(TraconImageProviderOptions.Model),
                ref failures);

            var scope = $"{nameof(TraconImageOptions)}.{nameof(TraconImageOptions.Providers)}[{providerName}]";

            if (provider.MaxRetries is { } maxRetries)
            {
                if (!ImageProviderSettingKeys.SupportsMaxRetries(providerName))
                {
                    // Nothing is ignored silently: a ceiling the client cannot apply would read as a protection that is not there.
                    (failures ??= []).Add(
                        $"{scope}.{nameof(TraconImageProviderOptions.MaxRetries)} is set, but the '{providerName}' image provider has no " +
                        "retry setting, so the value would be ignored. Only the openai and azure-openai image providers take one: remove it.");
                }
                else if (maxRetries is < 0 or > ImageProviderSettingKeys.MaximumMaxRetries)
                {
                    (failures ??= []).Add(
                        $"{scope}.{nameof(TraconImageProviderOptions.MaxRetries)} must be from 0 to {ImageProviderSettingKeys.MaximumMaxRetries}. " +
                        $"Received value: {maxRetries}.");
                }
            }

            try
            {
                ImageProviderSettingKeys.Validate(providerName, provider.ProviderSettings);
            }
            catch (TraconException exception)
            {
                (failures ??= []).Add($"{nameof(TraconImageOptions)}.{nameof(TraconImageOptions.Providers)}[{providerName}]: {exception.Message}");
            }
        }

        if (options.Enabled)
        {
            var defaultProvider = ImageProviderSelection.DefaultProviderName(options, _registrations);

            if (defaultProvider is null)
            {
                (failures ??= []).Add(
                    _registrations.Count > 1
                        ? $"{nameof(TraconImageOptions)}.{nameof(TraconImageOptions.Provider)} is required when {nameof(TraconImageOptions.Enabled)} is true " +
                          $"and more than one image provider is registered ({string.Join(", ", _registrations.Select(static r => r.Name))}): " +
                          $"name the one the tool draws with ({TraconImageOptions.SectionName}:Provider)."
                        : $"{nameof(TraconImageOptions)}.{nameof(TraconImageOptions.Provider)} is required when {nameof(TraconImageOptions.Enabled)} is true " +
                          "and no image provider extension is registered; name the provider of your own generator.");
            }
            else if (!options.Providers.TryGetValue(defaultProvider, out var chosen) || string.IsNullOrEmpty(chosen.Model))
            {
                (failures ??= []).Add(
                    $"{nameof(TraconImageOptions)}.{nameof(TraconImageOptions.Providers)}[{defaultProvider}].{nameof(TraconImageProviderOptions.Model)} is required when " +
                    $"{nameof(TraconImageOptions.Enabled)} is true.");
            }
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
