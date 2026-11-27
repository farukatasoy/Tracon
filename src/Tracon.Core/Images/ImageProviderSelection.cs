using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>An image provider the host registered with one of the <c>Use*Images</c> calls.</summary>
/// <param name="Name">The stable provider name: <c>openai</c>, <c>azure-openai</c> or <c>google</c>.</param>
/// <remarks>
/// The registration is what lets the options say which provider is the default without the host naming it when it registers
/// only one, and lets the validator count them.
/// </remarks>
internal sealed record ImageProviderRegistration(string Name);

/// <summary>What one image call is made with: the provider, its model and its settings.</summary>
/// <param name="Provider">The provider name.</param>
/// <param name="Model">The image model, or the Azure deployment.</param>
/// <param name="ProviderSettings">The provider's settings with the call's laid over them; empty when there are none.</param>
/// <param name="MaxRetries">The ceiling on how often the provider's client sends this call again, or <see langword="null"/> for none.</param>
internal sealed record ImageSelection(
    string Provider,
    string Model,
    IReadOnlyDictionary<string, JsonElement> ProviderSettings,
    int? MaxRetries = null)
{
    /// <summary>Builds the options of one generation call: the model, the provider's settings and the retry ceiling.</summary>
    /// <param name="count">How many images.</param>
    /// <param name="size">The image size, or <see langword="null"/> for the provider's own.</param>
    /// <returns>The options.</returns>
#pragma warning disable MEAI001
    public Microsoft.Extensions.AI.ImageGenerationOptions ToOptions(int count, System.Drawing.Size? size)
    {
        var options = new Microsoft.Extensions.AI.ImageGenerationOptions
        {
            Count = count,
            ImageSize = size,
            ModelId = Model,
        };

        if (ProviderSettings.Count > 0 || MaxRetries is not null)
        {
            options.AdditionalProperties = [];

            // The values stay JsonElements: a generator reads the key it supports and refuses a value of another type.
            foreach (var (key, value) in ProviderSettings)
            {
                options.AdditionalProperties[key] = value;
            }

            // Not a provider setting: no call can name it, and it is written last so that nothing replaces it.
            if (MaxRetries is { } ceiling)
            {
                options.AdditionalProperties[ImageProviderSettingKeys.MaxRetriesProperty] = ceiling;
            }
        }

        return options;
    }
#pragma warning restore MEAI001
}

/// <summary>
/// Reads the image options on every call and says which provider, model and settings the call uses.
/// </summary>
/// <remarks>
/// <para>
/// One reader for the tool, the operator endpoint and the media services, so none of them can read the provider from one place
/// and the model from another. The selection of a call is made once and carried through it, so the model of one provider is
/// never given to the request of another. Like every Tracon option the values are read when the host starts; a host that adds
/// a change token source of its own gets the new values on the next call.
/// </para>
/// </remarks>
internal sealed class ImageProviderSelection
{
    private readonly IOptionsMonitor<TraconImageOptions> _options;
    private readonly IReadOnlyList<ImageProviderRegistration> _registrations;

    public ImageProviderSelection(IOptionsMonitor<TraconImageOptions> options, IEnumerable<ImageProviderRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(registrations);

        _options = options;
        _registrations = [.. registrations];
    }

    /// <summary>Gets the options as they are now.</summary>
    public TraconImageOptions Options => _options.CurrentValue;

    /// <summary>Gets the name of the default provider.</summary>
    /// <exception cref="TraconException">No default provider can be told: none is named and the registrations do not say.</exception>
    public string DefaultProvider()
        => DefaultProviderName(Options, _registrations)
           ?? throw new TraconException(
               $"No default image provider is set. Name it in {TraconImageOptions.SectionName}:Provider " +
               $"({(_registrations.Count > 1 ? string.Join(", ", _registrations.Select(static r => r.Name)) : "the name of your generator's provider")}).");

    /// <summary>Selects the provider, model and settings of a call.</summary>
    /// <param name="provider">The provider the call asks for, or <see langword="null"/> for the default.</param>
    /// <param name="model">The model the call asks for, or <see langword="null"/> for the provider's own.</param>
    /// <param name="callSettings">
    /// The provider settings of this call, or <see langword="null"/> for none. They are laid over the settings registered for the
    /// provider and win for this call only.
    /// </param>
    /// <returns>The selection.</returns>
    /// <exception cref="TraconException">
    /// The provider is not registered, no model is set for it, or a setting is not supported by the provider or is of the wrong
    /// type. The message names the provider or the key and never a value.
    /// </exception>
    public ImageSelection Select(string? provider = null, string? model = null, IEnumerable<KeyValuePair<string, JsonElement>>? callSettings = null)
    {
        var options = Options;
        var requested = string.IsNullOrWhiteSpace(provider) ? DefaultProvider() : provider;

        // A provider the host named in Provider may be its own generator, which no extension registered; any other name must be
        // a registered one, so a typo in a call does not fall back to the default or to nothing. The name that comes back is the
        // registered spelling: the generator is looked up by it, the tenant's policy is asked about it and the cost is priced
        // under it, and none of them may be handed the caller's own letter case.
        var name = KnownName(requested, options)
            ?? throw new TraconException(
                $"The image provider '{requested}' is not registered. Registered: {string.Join(", ", KnownNames(options))}.");

        options.Providers.TryGetValue(name, out var configured);

        var chosenModel = string.IsNullOrWhiteSpace(model) ? configured?.Model : model;

        if (string.IsNullOrWhiteSpace(chosenModel))
        {
            throw new TraconException(
                $"No image model is set for provider '{name}'. Set {TraconImageOptions.SectionName}:Providers:{name}:Model.");
        }

        var settings = Merge(configured?.ProviderSettings, callSettings);

        // On every call, not at start alone: the call's own settings were never checked. The registered ones were, so a failure
        // here is the call's.
        ImageProviderSettingKeys.Validate(name, settings);

        return new ImageSelection(name, chosenModel, settings, configured?.MaxRetries);
    }

    /// <summary>
    /// Lays the settings of a call over the ones registered for the provider, in a dictionary of this call alone.
    /// </summary>
    /// <param name="registered">The provider's registered settings, or <see langword="null"/> when it has none.</param>
    /// <param name="call">The call's settings, or <see langword="null"/> for none.</param>
    /// <returns>The merged settings; a key in both takes the call's value, whatever its letter case.</returns>
    /// <remarks>
    /// A new dictionary every time: the registered settings are shared by every call and a call must not change them, and
    /// two calls at once must not see each other's.
    /// </remarks>
    private static IReadOnlyDictionary<string, JsonElement> Merge(
        IEnumerable<KeyValuePair<string, JsonElement>>? registered,
        IEnumerable<KeyValuePair<string, JsonElement>>? call)
    {
        var merged = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in registered ?? [])
        {
            merged[key] = value;
        }

        foreach (var (key, value) in call ?? [])
        {
            merged[key] = value;
        }

        return merged.Count == 0
            ? System.Collections.ObjectModel.ReadOnlyDictionary<string, JsonElement>.Empty
            : merged;
    }

    /// <summary>Names the default provider of an option set, or <see langword="null"/> when it cannot be told.</summary>
    /// <param name="options">The options.</param>
    /// <param name="registrations">The registered image providers.</param>
    /// <returns>The named provider; else the only registered one; else <see langword="null"/>.</returns>
    internal static string? DefaultProviderName(TraconImageOptions options, IReadOnlyList<ImageProviderRegistration> registrations)
        => !string.IsNullOrWhiteSpace(options.Provider)
            ? options.Provider
            : registrations.Count == 1 ? registrations[0].Name : null;

    private string? KnownName(string name, TraconImageOptions options)
        => _registrations.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))?.Name
           ?? (string.Equals(options.Provider, name, StringComparison.OrdinalIgnoreCase) ? options.Provider : null);

    private IEnumerable<string> KnownNames(TraconImageOptions options)
        => _registrations.Select(static r => r.Name)
            .Concat(string.IsNullOrWhiteSpace(options.Provider) ? [] : [options.Provider])
            .Distinct(StringComparer.OrdinalIgnoreCase);
}

/// <summary>The provider settings the image providers support, and the check that holds a set to them.</summary>
internal static class ImageProviderSettingKeys
{
    /// <summary>
    /// The key under which a call's retry ceiling travels in <c>ImageGenerationOptions.AdditionalProperties</c>: a whole number.
    /// Core writes it from <see cref="TraconImageProviderOptions.MaxRetries"/> and the OpenAI-client generators read it. It is
    /// not a provider setting: no call can supply it, and the check of <see cref="Validate"/> refuses it as an unknown key.
    /// </summary>
    public const string MaxRetriesProperty = "tracon.maxRetries";

    /// <summary>The most a retry ceiling may be.</summary>
    public const int MaximumMaxRetries = 10;

    /// <summary>The OpenAI image quality: a string such as <c>low</c>, <c>medium</c>, <c>high</c> or <c>auto</c>.</summary>
    public const string OpenAIQuality = "openai.quality";

    /// <summary>The Azure OpenAI image quality, the same string.</summary>
    public const string AzureOpenAIQuality = "azure-openai.quality";

    /// <summary>Returns the full keys a provider supports.</summary>
    /// <param name="provider">The provider name.</param>
    /// <returns>The keys; empty when the provider supports none.</returns>
    public static IReadOnlyCollection<string> Supported(string provider)
        => provider.ToUpperInvariant() switch
        {
            "OPENAI" => [OpenAIQuality],
            "AZURE-OPENAI" => [AzureOpenAIQuality],
            _ => [],
        };

    /// <summary>Gets whether a provider's image client can be given a retry ceiling.</summary>
    /// <param name="provider">The provider name.</param>
    /// <returns>
    /// <see langword="true"/> for the providers whose generator is written over the OpenAI client library (<c>openai</c> and
    /// <c>azure-openai</c>); <see langword="false"/> for the others, whose client has no retry setting to lower.
    /// </returns>
    public static bool SupportsMaxRetries(string provider)
        => provider.ToUpperInvariant() is "OPENAI" or "AZURE-OPENAI";

    /// <summary>Checks a provider's settings: every key is supported and every value is of the type the key takes.</summary>
    /// <param name="provider">The provider name.</param>
    /// <param name="settings">The settings.</param>
    /// <exception cref="TraconException">A key is not supported, or a value is not text. The message never carries a value.</exception>
    public static void Validate(string provider, IEnumerable<KeyValuePair<string, JsonElement>> settings)
    {
        var copy = new Dictionary<string, JsonElement>(settings, StringComparer.OrdinalIgnoreCase);

        if (copy.Count == 0)
        {
            return;
        }

        ModelProviderSettings.Validate(copy, provider, Supported(provider));

        // Both supported keys take text. A value of another type would reach the request as something the provider refuses
        // with its own words.
        foreach (var (key, value) in copy)
        {
            if (value.ValueKind != JsonValueKind.String)
            {
                throw new TraconException($"The image setting '{key}' must be a string.");
            }
        }
    }
}
