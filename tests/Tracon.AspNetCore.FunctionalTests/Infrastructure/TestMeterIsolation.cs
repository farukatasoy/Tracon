using System.Diagnostics.Metrics;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Serves one <see cref="Meter"/> instance to every consumer in a test's host, so a
/// listener can target it by reference instead of by name.
/// </summary>
/// <remarks>
/// 🚨 <see cref="MeterListener"/> is process-wide and xUnit runs test classes in the
/// same assembly in parallel. A listener that enables instruments by
/// <c>instrument.Meter.Name</c> also receives measurements from a DIFFERENT
/// <see cref="Meter"/> instance carrying the same name in another test running at the
/// same moment — which is how a real failure landed. Register this as
/// <see cref="IMeterFactory"/> on the host (<c>TraconTestHost.StartAsync</c>'s
/// <c>configureServices</c>); <c>TraconMetrics</c> resolves
/// <see cref="IMeterFactory"/> from DI, so it will publish onto <see cref="Meter"/>.
/// <c>MeterListenerIsolationTests</c> enforces this repository-wide.
/// </remarks>
internal sealed class TestMeterFactory : IMeterFactory
{
    public Meter Meter { get; } = new(TraconDiagnostics.MeterName);

    public Meter Create(MeterOptions options) => Meter;

    public void Dispose() => Meter.Dispose();
}
