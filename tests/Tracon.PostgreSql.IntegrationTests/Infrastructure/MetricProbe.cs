using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;

namespace Tracon.PostgreSql.IntegrationTests.Infrastructure;

/// <summary>
/// Owns a <see cref="TraconMetrics"/> over a private <see cref="Meter"/> and collects
/// every measurement published on it.
/// </summary>
/// <remarks>
/// The <see cref="MetricCollector"/> matches the meter by REFERENCE, not by name. Tracon's
/// meter name is a constant shared by every instrument in the process, so a name match
/// would also collect measurements from an unrelated test running beside this one and turn
/// an "it was counted once" assertion into a race.
/// </remarks>
internal sealed class MetricProbe : IDisposable
{
    private readonly Meter _meter = new(TraconDiagnostics.MeterName);
    private readonly MetricCollector _collector;

    public MetricProbe()
    {
        Metrics = new TraconMetrics(new SingleMeterFactory(_meter), new TenantTaggedOptions());
        _collector = new MetricCollector(_meter);
    }

    /// <summary>Gets the metric set to hand to the code under test.</summary>
    public TraconMetrics Metrics { get; }

    /// <summary>Returns the tags of every measurement published on <paramref name="name"/>.</summary>
    public List<IReadOnlyDictionary<string, object?>> TagsFor(string name) => _collector.Tags(name);

    public void Dispose()
    {
        _collector.Dispose();
        Metrics.Dispose();
        _meter.Dispose();
    }

    private sealed class SingleMeterFactory(Meter meter) : IMeterFactory
    {
        public Meter Create(MeterOptions options) => meter;

        public void Dispose()
        {
            // The meter is owned by the probe, which disposes it.
        }
    }

    /// <summary>
    /// The options that switch <c>tracon.tenant.id</c> on: the tag is off by default and
    /// these tests read it.
    /// </summary>
    private sealed class TenantTaggedOptions : IOptionsMonitor<TraconOptions>
    {
        public TraconOptions CurrentValue { get; } = new() { Observability = { IncludeTenantTag = true } };

        public TraconOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<TraconOptions, string?> listener) => null;
    }
}
