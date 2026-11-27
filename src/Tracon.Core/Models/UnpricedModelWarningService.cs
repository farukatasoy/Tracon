using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Reports, once while the host starts, the catalog models that carry no price
/// and therefore make every run they answer cost nothing that can be reported.
/// </summary>
/// <remarks>
/// <para>
/// Tracon deliberately ships no built-in model list and no built-in prices
/// on purpose: names and rates change far faster than a NuGet package is released,
/// and an embedded table becomes a confident lie within weeks. The price comes
/// from the catalog entry or from <c>Tracon:Pricing</c>, and when neither has
/// one the run is recorded with <see cref="PricingSource.Unknown"/> and empty
/// cost columns — <strong>not</strong> zero, because zero would claim the model
/// is free.
/// </para>
/// <para>
/// That mechanism is correct and it stays. What was missing is that it said so
/// nowhere. An installation whose catalog carried no prices at all recorded
/// every single run without a cost, and the only way to find out why was to
/// read the <c>pricing_source</c> enum on a row. This warning names the models
/// and the one setting that fixes them.
/// </para>
/// <para>
/// It never throws and never blocks startup. Running without prices is a
/// legitimate choice — an installation that does not care about cost
/// attribution, or one that recalculates later with
/// <c>RunCostRecalculationService</c> once the rates are known.
/// </para>
/// <para>
/// The speech, transcription and image models the host configured are never in a catalog, so a second line names them
/// (<see cref="UnpricedMediaModels"/>). Neither line reads a saved definition: those belong to a tenant, and the line a run
/// writes when it finds no price covers them.
/// </para>
/// </remarks>
/// <param name="providers">The registered providers and their catalogs, read once at startup.</param>
/// <param name="options">The Tracon options, for the <c>Tracon:Pricing</c> fallback.</param>
/// <param name="logger">The logger the warning is written to.</param>
/// <param name="mediaModelSources">The sources of the speech and image models the host configured. Optional.</param>
internal sealed partial class UnpricedModelWarningService(
    IEnumerable<IModelProvider> providers,
    IOptions<TraconOptions> options,
    ILogger<UnpricedModelWarningService> logger,
    IEnumerable<IConfiguredMediaModelSource>? mediaModelSources = null) : ITraconStartupCheck
{
    /// <inheritdoc />
    public StartupCheckKind Kind => StartupCheckKind.Advisory;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!logger.IsEnabled(LogLevel.Warning))
        {
            return Task.CompletedTask;
        }

        var pricing = options.Value.Pricing;
        var unpriced = UnpricedModels.Find(providers, pricing);

        if (unpriced.Count > 0)
        {
            NoPriceForModels(logger, unpriced.Count, string.Join(", ", unpriced));
        }

        var unpricedMedia = UnpricedMediaModels.Find(mediaModelSources ?? [], pricing);

        if (unpricedMedia.Count > 0)
        {
            NoPriceForMediaModels(logger, unpricedMedia.Count, string.Join(", ", unpricedMedia));
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "{Count} catalog model(s) carry no price, so every run they answer is recorded with empty cost " +
                  "columns and PricingSource.Unknown — this is honest, not zero, but no cost report can use it. " +
                  "Set a price on the catalog entry (InputCostPerMillionTokens / OutputCostPerMillionTokens) or " +
                  "under Tracon:Pricing:<provider>:<model>:Input and :Output, then recalculate past runs if you need them. " +
                  "Unpriced: {Models}")]
    private static partial void NoPriceForModels(ILogger logger, int count, string models);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "{Count} configured speech, transcription or image model(s) carry no price, so every call they answer is " +
                  "recorded without a cost: no cost report can use it. These models are not in any catalog. Set " +
                  "Tracon:Pricing:Voice:<provider>:<model>:PerMillionCharacters (synthesis) or :PerMinute (transcription), or " +
                  "Tracon:Pricing:Images:<provider>:<model>:PerImage or :OutputCostPerMillionTokens. Unpriced: {Models}")]
    private static partial void NoPriceForMediaModels(ILogger logger, int count, string models);
}
