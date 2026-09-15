using System.Diagnostics.Metrics;

namespace Tracon.PostgreSql.IntegrationTests.Infrastructure;

/// <summary>
/// Owns a <see cref="TraconMetrics"/> over a private <see cref="Meter"/> and collects
/// every long measurement published on it.
/// </summary>
/// <remarks>
/// The listener matches its meter by REFERENCE, not by name. Tracon's meter name is a
/// constant shared by every instrument in the process, so a name match would also
/// collect measurements from an unrelated test running beside this one and turn an
/// "it was counted once" assertion into a race.
/// </remarks>
internal sealed class MetricProbe : IDisposable
{
    private readonly Meter _meter = new(TraconDiagnostics.MeterName);
    private readonly MeterListener _listener = new();
    private readonly List<(string Name, long Value, Dictionary<string, object?> Tags)> _measurements = [];
    private readonly Lock _gate = new();

    public MetricProbe()
    {
        Metrics = new TraconMetrics(new SingleMeterFactory(_meter));

        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter, _meter))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            // The TagList is a ref struct over the caller's stack; it is copied
            // here because reading it after the callback returns is undefined.
            var copied = new Dictionary<string, object?>(tags.Length, StringComparer.Ordinal);

            foreach (var tag in tags)
            {
                copied[tag.Key] = tag.Value;
            }

            lock (_gate)
            {
                _measurements.Add((instrument.Name, value, copied));
            }
        });

        _listener.Start();
    }

    /// <summary>Gets the metric set to hand to the code under test.</summary>
    public TraconMetrics Metrics { get; }

    /// <summary>Returns the tags of every measurement published on <paramref name="name"/>.</summary>
    public List<IReadOnlyDictionary<string, object?>> TagsFor(string name)
    {
        lock (_gate)
        {
            return
            [
                .. _measurements
                    .Where(measurement => string.Equals(measurement.Name, name, StringComparison.Ordinal))
                    .Select(measurement => (IReadOnlyDictionary<string, object?>)measurement.Tags),
            ];
        }
    }

    public void Dispose()
    {
        _listener.Dispose();
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
}
