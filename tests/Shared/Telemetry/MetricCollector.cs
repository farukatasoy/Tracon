using System.Diagnostics.Metrics;
using System.Numerics;

namespace Tracon.Tests.Common;

/// <summary>One measurement a <see cref="MetricCollector"/> recorded.</summary>
/// <param name="Instrument">The name of the instrument that published it.</param>
/// <param name="Value">
/// The value, widened to <see cref="double"/>. A count stays exact up to 2^53, far beyond anything a test counts;
/// <paramref name="ValueType"/> says what the instrument measured.
/// </param>
/// <param name="ValueType">The type the instrument measures: <see cref="long"/>, <see cref="double"/>, <see cref="int"/>, ...</param>
/// <param name="Tags">The tags of the measurement, copied out of the callback that reported it.</param>
internal sealed record RecordedMeasurement(string Instrument, double Value, Type ValueType, IReadOnlyDictionary<string, object?> Tags)
{
    /// <summary>Gets whether the measurement carries the tag <paramref name="key"/> with exactly <paramref name="value"/>.</summary>
    /// <param name="key">The tag key.</param>
    /// <param name="value">The value the tag must hold.</param>
    /// <returns><see langword="true"/> when the tag is present and equal.</returns>
    public bool HasTag(string key, object? value) => Tags.TryGetValue(key, out var actual) && object.Equals(actual, value);
}

/// <summary>
/// Records the measurements of a meter's instruments, with their tags, so a test can assert on them or wait for one.
/// </summary>
/// <remarks>
/// <para>
/// Bind a collector to a <see cref="Meter"/> INSTANCE (<see cref="MetricCollector(Meter)"/>), never to a meter name. A
/// <see cref="MeterListener"/> is process-wide and test classes run in parallel: a listener that selects by meter name also
/// receives what a different <see cref="Meter"/> with the same name publishes in another test at the same moment, and every
/// "there is exactly one measurement" assertion then depends on what else is running. Hand the code under test its own
/// <see cref="IMeterFactory"/> and pass the meter it serves.
/// </para>
/// <para>
/// <see cref="ForInstruments"/> is the exception for a meter nobody can inject, such as the one a library builds internally:
/// it selects by the name of the instrument, so it also records what other tests publish under that name. A test that uses it
/// discriminates by a value only that test holds (a tag, a marker in a tag) and never asserts an exact count of everything.
/// </para>
/// <para>
/// A callback copies the tags it is given, because the span the runtime passes in is a view over the caller's stack and is
/// undefined once the callback returns. The collector is thread safe: measurements may arrive from any thread.
/// </para>
/// </remarks>
internal sealed class MetricCollector : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly List<RecordedMeasurement> _measurements = [];
    private readonly Lock _gate = new();

    /// <summary>Starts recording every instrument of <paramref name="meter"/>, matched by reference.</summary>
    /// <param name="meter">The meter instance the code under test publishes on.</param>
    public MetricCollector(Meter meter)
        : this(Selecting(meter))
    {
    }

    private MetricCollector(Func<Instrument, bool> selects)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (selects(instrument))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        // A listener drops the measurements of an instrument whose value type has no callback, so every type an instrument can measure has one.
        Listen<byte>();
        Listen<short>();
        Listen<int>();
        Listen<long>();
        Listen<float>();
        Listen<double>();
        Listen<decimal>();

        _listener.Start();
    }

    /// <summary>Starts recording the instruments that carry one of <paramref name="instrumentNames"/>, on any meter of the process.</summary>
    /// <param name="instrumentNames">The instrument names to record.</param>
    /// <returns>The collector. See the class remarks for what selecting by name costs.</returns>
    public static MetricCollector ForInstruments(params string[] instrumentNames)
    {
        ArgumentNullException.ThrowIfNull(instrumentNames);

        var names = new HashSet<string>(instrumentNames, StringComparer.Ordinal);

        return new MetricCollector(instrument => names.Contains(instrument.Name));
    }

    /// <summary>Gets every measurement recorded so far, in the order it arrived.</summary>
    /// <returns>A copy; later measurements do not change it.</returns>
    public List<RecordedMeasurement> Snapshot()
    {
        lock (_gate)
        {
            return [.. _measurements];
        }
    }

    /// <summary>Gets the measurements recorded so far for one instrument, in the order they arrived.</summary>
    /// <param name="instrument">The instrument name.</param>
    /// <returns>A copy; later measurements do not change it.</returns>
    public List<RecordedMeasurement> Snapshot(string instrument)
    {
        lock (_gate)
        {
            return [.. _measurements.Where(measurement => string.Equals(measurement.Instrument, instrument, StringComparison.Ordinal))];
        }
    }

    /// <summary>Gets the values recorded so far for one instrument.</summary>
    /// <param name="instrument">The instrument name.</param>
    /// <returns>The values, in the order they arrived.</returns>
    public List<double> Values(string instrument) => [.. Snapshot(instrument).Select(static measurement => measurement.Value)];

    /// <summary>Gets the tags of every measurement recorded so far for one instrument.</summary>
    /// <param name="instrument">The instrument name.</param>
    /// <returns>One tag set per measurement, in the order the measurements arrived.</returns>
    public List<IReadOnlyDictionary<string, object?>> Tags(string instrument) => [.. Snapshot(instrument).Select(static measurement => measurement.Tags)];

    /// <summary>Gets every measurement recorded so far and forgets them.</summary>
    /// <returns>The measurements, in the order they arrived.</returns>
    public List<RecordedMeasurement> Drain()
    {
        lock (_gate)
        {
            var taken = _measurements.ToList();
            _measurements.Clear();

            return taken;
        }
    }

    /// <summary>Forgets what was recorded, polls the observable instruments, and returns what one of them reported.</summary>
    /// <param name="instrument">The instrument name.</param>
    /// <returns>The measurements the poll produced for that instrument.</returns>
    public List<RecordedMeasurement> Scrape(string instrument)
    {
        lock (_gate)
        {
            _measurements.Clear();
        }

        _listener.RecordObservableInstruments();

        return Snapshot(instrument);
    }

    /// <summary>Waits until a measurement is recorded for one instrument.</summary>
    /// <remarks>
    /// A measurement written by a background worker or on run completion need not exist by the time a request returns, so this
    /// polls with <see cref="WaitUntil"/> instead of asserting at once or sleeping.
    /// </remarks>
    /// <param name="instrument">The instrument name.</param>
    /// <param name="accept">Which measurements count; every measurement of the instrument when omitted.</param>
    /// <param name="timeout">The failure bound; <see cref="WaitUntil.DefaultTimeout"/> when omitted.</param>
    /// <param name="cancellationToken">Ends the wait early; the current test's token when omitted.</param>
    /// <returns>The first measurement that counts.</returns>
    /// <exception cref="TimeoutException">No such measurement was recorded within the bound.</exception>
    public async Task<RecordedMeasurement> WaitForAsync(
        string instrument,
        Func<RecordedMeasurement, bool>? accept = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var matching = await WaitUntil.ValueAsync(
            () => Task.FromResult(Snapshot(instrument).Where(measurement => accept is null || accept(measurement)).ToList()),
            static measurements => measurements.Count > 0,
            $"a measurement named '{instrument}' (the instrument may not be wired into the code under test)",
            timeout,
            cancellationToken);

        return matching[0];
    }

    /// <inheritdoc />
    public void Dispose() => _listener.Dispose();

    private static Func<Instrument, bool> Selecting(Meter meter)
    {
        ArgumentNullException.ThrowIfNull(meter);

        return instrument => ReferenceEquals(instrument.Meter, meter);
    }

    private void Listen<T>()
        where T : struct, INumber<T>
        => _listener.SetMeasurementEventCallback<T>((instrument, value, tags, _) => Record(instrument.Name, double.CreateChecked(value), typeof(T), tags));

    private void Record(string instrument, double value, Type valueType, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copied = new Dictionary<string, object?>(tags.Length, StringComparer.Ordinal);

        foreach (var tag in tags)
        {
            copied[tag.Key] = tag.Value;
        }

        lock (_gate)
        {
            _measurements.Add(new RecordedMeasurement(instrument, value, valueType, copied));
        }
    }
}
