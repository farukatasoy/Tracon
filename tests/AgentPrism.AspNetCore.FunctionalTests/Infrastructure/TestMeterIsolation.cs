using System.Diagnostics.Metrics;

namespace AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Serves one <see cref="Meter"/> instance to every consumer in a test's host, so a
/// listener can target it by reference instead of by name.
/// </summary>
/// <remarks>
/// 🚨 <see cref="MeterListener"/> is process-wide and xUnit runs test classes in the
/// same assembly in parallel. A listener that enables instruments by
/// <c>instrument.Meter.Name</c> also receives measurements from a DIFFERENT
/// <see cref="Meter"/> instance carrying the same name in another test running at the
/// same moment — which is how a real failure landed in phase 134. Register this as
/// <see cref="IMeterFactory"/> on the host (<c>AgentPrismTestHost.StartAsync</c>'s
/// <c>configureServices</c>); <c>AgentPrismMetrics</c> resolves
/// <see cref="IMeterFactory"/> from DI, so it will publish onto <see cref="Meter"/>.
/// <c>MeterListenerIsolationTests</c> enforces this repository-wide.
/// </remarks>
internal sealed class TestMeterFactory : IMeterFactory
{
    public Meter Meter { get; } = new(AgentPrismDiagnostics.MeterName);

    public Meter Create(MeterOptions options) => Meter;

    public void Dispose() => Meter.Dispose();
}

/// <summary>
/// Collects the measurements published on one specific <see cref="Meter"/> instance,
/// and waits for one to arrive.
/// </summary>
/// <remarks>
/// A measurement recorded by a background worker or by run completion does not
/// necessarily exist by the time the HTTP response returns, so
/// <see cref="WaitForLongAsync"/> polls rather than asserting immediately.
/// </remarks>
internal sealed class MeterInstanceCollector : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly List<Measurement> _measurements = [];
    private readonly Lock _gate = new();

    public MeterInstanceCollector(Meter meter)
    {
        ArgumentNullException.ThrowIfNull(meter);

        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter, meter))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Add(instrument.Name, value, tags));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Add(instrument.Name, value, tags));

        _listener.Start();
    }

    /// <summary>One measurement: its value as a <see cref="double"/> and its tags.</summary>
    internal sealed record Measurement(string Name, double Value, Dictionary<string, object?> Tags);

    /// <summary>Every measurement published under <paramref name="name"/> so far.</summary>
    public List<Measurement> Snapshot(string name)
    {
        lock (_gate)
        {
            return [.. _measurements.Where(m => string.Equals(m.Name, name, StringComparison.Ordinal))];
        }
    }

    /// <summary>Waits until at least one measurement is published under <paramref name="name"/>.</summary>
    /// <param name="name">The instrument name.</param>
    /// <param name="timeout">How long to wait. Defaults to thirty seconds.</param>
    /// <returns>The first matching measurement.</returns>
    /// <remarks>
    /// No tag predicate is offered on purpose. Because this collector is bound to
    /// ONE <see cref="Meter"/> instance, every measurement it sees belongs to this
    /// test's own host — discriminating by a tag value would only re-implement, less
    /// reliably, the isolation the instance binding already gives.
    /// </remarks>
    public async Task<Measurement> WaitForAsync(string name, TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (Snapshot(name) is [var first, ..])
            {
                return first;
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        throw new InvalidOperationException(
            $"No measurement named '{name}' was published within the timeout. " +
            "The instrument may not be wired into the real DI chain.");
    }

    /// <summary>Polls the observable instruments and returns what <paramref name="name"/> reported.</summary>
    /// <param name="name">The instrument name.</param>
    /// <returns>The measurements published during this poll.</returns>
    public List<Measurement> Scrape(string name)
    {
        lock (_gate)
        {
            _measurements.Clear();
        }

        _listener.RecordObservableInstruments();

        return Snapshot(name);
    }

    public void Dispose() => _listener.Dispose();

    private void Add(string name, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var tag in tags)
        {
            dictionary[tag.Key] = tag.Value;
        }

        lock (_gate)
        {
            _measurements.Add(new Measurement(name, value, dictionary));
        }
    }
}
