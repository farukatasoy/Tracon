using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon;

/// <summary>
/// Says once for each model and kind of call that a speech, transcription, live voice or image call found no price.
/// </summary>
/// <remarks>
/// <para>
/// The catalog warning cannot cover these calls: a voice or image model is never in a provider's catalog, so the startup
/// warning does not list it, and the lookup that finds no price used to return <see langword="null"/> without a word. A
/// model that is billed and recorded without a cost reads exactly like a cost report that is broken.
/// </para>
/// <para>
/// The line names the provider and the model and nothing else: a model a tenant brought with its own provider credentials
/// reaches the same lookups, and no tenant identifier belongs in a line every operator reads. A lookup that names no
/// provider or no model says nothing, because it cannot name what to price. The set of lines already written is bounded, so
/// a flood of distinct model names a tenant chooses cannot grow it without limit.
/// </para>
/// </remarks>
internal sealed partial class UnpricedMediaWarnings
{
    // One line for each (kind, provider, model). Bounded: beyond it nothing more is logged and nothing more is kept.
    private const int MaxWarnedModels = 256;

    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<(MediaPriceKind Kind, string Provider, string Model), byte> _warned = new();

    /// <summary>Initializes the warnings.</summary>
    /// <param name="logger">The logger the lines are written to. Optional: the price is looked up without it.</param>
    public UnpricedMediaWarnings(ILogger<UnpricedMediaWarnings>? logger = null)
        => _logger = logger ?? NullLogger<UnpricedMediaWarnings>.Instance;

    /// <summary>Says that a model has no price for a kind of call, the first time it is asked.</summary>
    /// <param name="kind">The kind of call.</param>
    /// <param name="provider">The provider name, or <see langword="null"/> when the lookup did not name one.</param>
    /// <param name="model">The model, or <see langword="null"/> when the lookup did not name one.</param>
    public void WarnOnce(MediaPriceKind kind, string? provider, string? model)
    {
        if (provider is not { Length: > 0 }
            || model is not { Length: > 0 }
            || !_logger.IsEnabled(LogLevel.Warning)
            || _warned.Count >= MaxWarnedModels
            || !_warned.TryAdd((kind, provider, model), 0))
        {
            return;
        }

        NoPrice(_logger, MediaPriceLookup.Label(kind), model, provider, MediaPriceLookup.Setting(kind, provider, model));
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "The {Kind} model {Model} of provider {Provider} has no price in Tracon:Pricing, so its calls are recorded " +
                  "without a cost: no cost report can use them. Set {Setting}. This is said once for each model.")]
    private static partial void NoPrice(ILogger logger, string kind, string model, string provider, string setting);
}
