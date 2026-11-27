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
/// reaches the same lookups, and no tenant identifier belongs in a line every operator reads.
/// </para>
/// <para>
/// A name is a caller's string: the operator image endpoint passes the model of the request to the lookup, and a generator
/// that does not check it lets any caller choose it. So a name never reaches the set or the log unchecked. A name longer
/// than a usage record holds (<see cref="NonTokenCostAccumulator.MaxProviderLength"/> and
/// <see cref="NonTokenCostAccumulator.MaxModelLength"/>) or holding a control character is neither kept nor written; one
/// line says that such a name was refused, and no later one adds another. The set of names already written is bounded, the
/// bound is checked and the name added in one step, and the first name beyond the bound writes one line that says naming has
/// stopped, so the next real unpriced model is not lost without a trace.
/// </para>
/// <para>
/// A lookup that names no provider or no model says nothing, because it cannot name what to price.
/// </para>
/// </remarks>
internal sealed partial class UnpricedMediaWarnings
{
    // The most (kind, provider, model) entries named in the log, for the life of the process.
    private const int MaxWarnedModels = 256;

    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private readonly HashSet<(MediaPriceKind Kind, string Provider, string Model)> _warned = [];
    private bool _refusedNameSaid;
    private bool _stoppedSaid;

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
        if (provider is not { Length: > 0 } || model is not { Length: > 0 } || !_logger.IsEnabled(LogLevel.Warning))
        {
            return;
        }

        if (!IsUsableName(provider, NonTokenCostAccumulator.MaxProviderLength)
            || !IsUsableName(model, NonTokenCostAccumulator.MaxModelLength))
        {
            WarnOnceThatANameWasRefused();

            return;
        }

        var key = (kind, provider, model);
        var named = false;
        var stopsNow = false;

        // One step: the check of the bound and the add cannot be separated by another caller, so the set never passes it.
        // What is written is decided here and written after the lock, outside it.
        lock (_gate)
        {
            if (_warned.Contains(key))
            {
                return;
            }

            if (_warned.Count >= MaxWarnedModels)
            {
                stopsNow = !_stoppedSaid;
                _stoppedSaid = true;
            }
            else
            {
                _ = _warned.Add(key);
                named = true;
            }
        }

        if (named)
        {
            NoPrice(_logger, MediaPriceLookup.Label(kind), model, provider, MediaPriceLookup.Setting(kind, provider, model));
        }
        else if (stopsNow)
        {
            NamingStopped(_logger, MaxWarnedModels);
        }
    }

    private static bool IsUsableName(string name, int maxLength)
    {
        if (name.Length > maxLength)
        {
            return false;
        }

        foreach (var character in name)
        {
            // A control character is a line break or an escape in a plain-text sink; U+2028 and U+2029 are line breaks to
            // the sinks that split on Unicode.
            if (char.IsControl(character) || character is '\u2028' or '\u2029')
            {
                return false;
            }
        }

        return true;
    }

    private void WarnOnceThatANameWasRefused()
    {
        lock (_gate)
        {
            if (_refusedNameSaid)
            {
                return;
            }

            _refusedNameSaid = true;
        }

        NameRefused(_logger, NonTokenCostAccumulator.MaxProviderLength, NonTokenCostAccumulator.MaxModelLength);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "The {Kind} model {Model} of provider {Provider} has no price in Tracon:Pricing, so its calls are recorded " +
                  "without a cost: no cost report can use them. Set {Setting}. This is said once for each model.")]
    private static partial void NoPrice(ILogger logger, string kind, string model, string provider, string setting);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "No further unpriced speech, transcription, live voice or image models will be named in the log; {Count} " +
                  "already were. A model with no price is still recorded without a cost. Read " +
                  "pricing.unpricedMediaModels in the diagnostics report for the models you configured.")]
    private static partial void NamingStopped(ILogger logger, int count);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "A speech, transcription, live voice or image call named a provider longer than {ProviderLimit} characters or " +
                  "a model longer than {ModelLimit} characters, or a name with a control character. Such a name is not " +
                  "logged and its model is not named. This is said once.")]
    private static partial void NameRefused(ILogger logger, int providerLimit, int modelLimit);
}
