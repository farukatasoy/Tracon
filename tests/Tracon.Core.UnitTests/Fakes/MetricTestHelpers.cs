using System.Diagnostics.Metrics;

namespace Tracon.Core.UnitTests.Fakes;

/// <summary>Serves the same <see cref="Meter"/> instance to every consumer, so a listener can target it by reference.</summary>
internal sealed class TestMeterFactory : IMeterFactory
{
    public Meter Meter { get; } = new(TraconDiagnostics.MeterName);

    public Meter Create(MeterOptions options) => Meter;

    public void Dispose() => Meter.Dispose();
}

/// <summary>Collects <c>long</c> measurements published on a specific <see cref="Meter"/> instance.</summary>
internal sealed class MetricCollector : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly List<(string Name, long Value, Dictionary<string, object?> Tags)> _longs = [];

    public MetricCollector(Meter meter)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter, meter))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            // The TagList is a ref struct over the caller's stack, so it is copied here
            // rather than stored: reading it after the callback returns is undefined.
            var copied = new Dictionary<string, object?>(tags.Length, StringComparer.Ordinal);

            foreach (var tag in tags)
            {
                copied[tag.Key] = tag.Value;
            }

            lock (_longs)
            {
                _longs.Add((instrument.Name, value, copied));
            }
        });

        _listener.Start();
    }

    public List<long> LongValues(string name)
    {
        lock (_longs)
        {
            return [.. _longs.Where(m => string.Equals(m.Name, name, StringComparison.Ordinal)).Select(m => m.Value)];
        }
    }

    /// <summary>Returns the tags of every measurement published on <paramref name="name"/>.</summary>
    public List<IReadOnlyDictionary<string, object?>> LongTags(string name)
    {
        lock (_longs)
        {
            return
            [
                .. _longs
                    .Where(m => string.Equals(m.Name, name, StringComparison.Ordinal))
                    .Select(m => (IReadOnlyDictionary<string, object?>)m.Tags),
            ];
        }
    }

    public void Dispose() => _listener.Dispose();
}
