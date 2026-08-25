using System.Diagnostics.Metrics;

namespace AgentPrism.Core.UnitTests.Fakes;

/// <summary>Serves the same <see cref="Meter"/> instance to every consumer, so a listener can target it by reference.</summary>
internal sealed class TestMeterFactory : IMeterFactory
{
    public Meter Meter { get; } = new(AgentPrismDiagnostics.MeterName);

    public Meter Create(MeterOptions options) => Meter;

    public void Dispose() => Meter.Dispose();
}

/// <summary>Collects <c>long</c> measurements published on a specific <see cref="Meter"/> instance.</summary>
internal sealed class MetricCollector : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly List<(string Name, long Value)> _longs = [];
    private readonly Lock _gate = new();

    public MetricCollector(Meter meter)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter, meter))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            lock (_gate)
            {
                _longs.Add((instrument.Name, value));
            }
        });

        _listener.Start();
    }

    public List<long> LongValues(string name)
    {
        lock (_gate)
        {
            return [.. _longs.Where(m => string.Equals(m.Name, name, StringComparison.Ordinal)).Select(m => m.Value)];
        }
    }

    public void Dispose() => _listener.Dispose();
}
