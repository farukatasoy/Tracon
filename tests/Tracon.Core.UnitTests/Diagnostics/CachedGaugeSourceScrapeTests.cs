using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Diagnostics;

/// <summary>
/// What a metrics scrape does when the options of the gauge cannot be read: it is answered, not failed.
/// </summary>
/// <remarks>
/// A host that adds a configuration source that reloads can make the options monitor throw on every read until a valid
/// value replaces it. The refresh loop already skips such a tick; the scrape asks the monitor on its own thread of control
/// and used to throw into the metrics pipeline.
/// </remarks>
public sealed class CachedGaugeSourceScrapeTests
{
    [Fact]
    public void A_scrape_whose_options_cannot_be_read_returns_the_last_snapshot_and_does_not_throw()
    {
        var monitor = new SwitchableOptionsMonitor<TraconOptions>(new TraconOptions());
        using var gauge = new ProbeGauge(monitor);

        gauge.Scrape().ShouldBeEmpty();

        monitor.Invalid = true;

        Should.NotThrow(gauge.Scrape);
        gauge.Scrape().ShouldBeEmpty();

        monitor.Invalid = false;

        gauge.Scrape().ShouldBeEmpty();
    }

    private sealed class ProbeGauge(IOptionsMonitor<TraconOptions> options)
        : CachedGaugeSource<int>(options, new SchemaReadyGate([]), timeProvider: null)
    {
        public IReadOnlyList<int> Scrape() => Snapshot();

        protected override bool IsEnabled(TraconObservabilityOptions observability) => true;

        protected override TimeSpan GetRefreshInterval(TraconObservabilityOptions observability) => TimeSpan.FromMinutes(1);

        protected override ValueTask<IReadOnlyList<int>> ReadAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult<IReadOnlyList<int>>([]);

        protected override void LogRefreshFailure(Exception exception)
        {
        }
    }
}
