using Microsoft.Extensions.Hosting;
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
/// </remarks>
/// <param name="providers">The registered providers and their catalogs, read once at startup.</param>
/// <param name="options">The Tracon options, for the <c>Tracon:Pricing</c> fallback.</param>
/// <param name="logger">The logger the warning is written to.</param>
internal sealed partial class UnpricedModelWarningService(
    IEnumerable<IModelProvider> providers,
    IOptions<TraconOptions> options,
    ILogger<UnpricedModelWarningService> logger) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!logger.IsEnabled(LogLevel.Warning))
        {
            return Task.CompletedTask;
        }

        var unpriced = UnpricedModels.Find(providers, options.Value.Pricing);

        if (unpriced.Count == 0)
        {
            return Task.CompletedTask;
        }

        NoPriceForModels(logger, unpriced.Count, string.Join(", ", unpriced));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "{Count} catalog model(s) carry no price, so every run they answer is recorded with empty cost " +
                  "columns and PricingSource.Unknown — this is honest, not zero, but no cost report can use it. " +
                  "Set a price on the catalog entry (InputCostPerMillionTokens / OutputCostPerMillionTokens) or " +
                  "under Tracon:Pricing:Providers, then recalculate past runs if you need them. Unpriced: {Models}")]
    private static partial void NoPriceForModels(ILogger logger, int count, string models);
}
