using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Names the image models the host configured, for the pricing diagnostics.</summary>
/// <remarks>
/// Reads <see cref="TraconImageOptions.Providers"/> only, and only while <see cref="TraconImageOptions.Enabled"/> is on: with
/// image generation off nothing draws with a model, so none is unpriced in any way that costs money.
/// </remarks>
/// <param name="options">The image options, read when the diagnostics are collected.</param>
internal sealed class ConfiguredImageModels(IOptions<TraconImageOptions> options) : IConfiguredMediaModelSource
{
    /// <inheritdoc />
    public IReadOnlyList<ConfiguredMediaModel> ConfiguredModels()
    {
        var images = options.Value;

        if (!images.Enabled)
        {
            return [];
        }

        return
        [
            .. images.Providers
                .Where(static entry => entry.Key is { Length: > 0 } && entry.Value.Model is { Length: > 0 })
                .Select(static entry => new ConfiguredMediaModel(MediaPriceKind.Image, entry.Key, entry.Value.Model!)),
        ];
    }
}
