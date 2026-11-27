using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Reports, once while the host starts, the keys under a model of <c>Tracon:Pricing</c> that no binder reads, so the price
/// they carry is silently ignored.
/// </summary>
/// <remarks>
/// <para>
/// The binder reads five short keys for a model (<c>Input</c>, <c>Output</c>, <c>CachedInput</c>, <c>CacheWrite</c>,
/// <c>CacheWriteLong</c>) and compared nothing else with them. A host that wrote <c>CacheRead</c> beside <c>Input</c> and
/// <c>Output</c> started, and its cached tokens were priced at the plain input rate for as long as nobody held the report up
/// to the provider's bill. A model written with the code property names alone is already refused by the options validator;
/// this check covers the models that have a valid rate and a second, unread one.
/// </para>
/// <para>
/// It never throws and never blocks startup: a configuration that has always started must keep starting, so this is a warning
/// and not a rejection.
/// </para>
/// </remarks>
/// <param name="options">The Tracon options, whose pricing section carries the keys the binder found.</param>
/// <param name="logger">The logger the warning is written to.</param>
internal sealed partial class UnrecognizedPricingKeyWarningService(
    IOptions<TraconOptions> options,
    ILogger<UnrecognizedPricingKeyWarningService> logger) : ITraconStartupCheck
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

        var keys = options.Value.Pricing.UnrecognizedKeys
            .DistinctBy(static key => key.Path, StringComparer.OrdinalIgnoreCase)
            .Select(Describe)
            .ToList();

        if (keys.Count == 0)
        {
            return Task.CompletedTask;
        }

        UnreadKeys(logger, keys.Count, string.Join(", ", PricingKeys.Known), string.Join("; ", keys));

        return Task.CompletedTask;
    }

    private static string Describe(UnrecognizedPricingKey key)
        => PricingKeys.LikelyMatch(key.Key) is { } match
            ? $"{key.Path} (did you mean {match}?)"
            : key.Path;

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "{Count} price key(s) under Tracon:Pricing are not read, so the price they carry is ignored. A model's " +
                  "rates are read from {Known}. A cached or cache-written token with no rate is priced at the plain input " +
                  "rate. Unread: {Keys}")]
    private static partial void UnreadKeys(ILogger logger, int count, string known, string keys);
}
