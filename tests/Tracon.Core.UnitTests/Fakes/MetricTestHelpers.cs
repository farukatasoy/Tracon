using System.Diagnostics.Metrics;

namespace Tracon.Core.UnitTests.Fakes;

/// <summary>
/// Serves the same <see cref="Meter"/> instance to every consumer, so a <see cref="MetricCollector"/> can target it by
/// reference.
/// </summary>
internal sealed class TestMeterFactory : IMeterFactory
{
    public Meter Meter { get; } = new(TraconDiagnostics.MeterName);

    public Meter Create(MeterOptions options) => Meter;

    public void Dispose() => Meter.Dispose();
}
