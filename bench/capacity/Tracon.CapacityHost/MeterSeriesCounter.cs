using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Tracon.CapacityHost;

/// <summary>Counts the distinct time series every meter in the process has produced.</summary>
/// <remarks>
/// <para>
/// 🚨 A metric whose tag set follows a caller-chosen value (a tenant, a run, an
/// agent name) grows one series per value, and a metric backend bills and slows
/// down by series. The process cannot see that from its own memory: the series
/// live in whatever consumes the instruments. This listener plays that
/// consumer and keeps only a 64-bit hash per distinct tag set, so the
/// measurement itself costs eight bytes a series and is capped.
/// </para>
/// <para>
/// Observable instruments publish only when asked, so <see cref="Observe"/>
/// calls <see cref="MeterListener.RecordObservableInstruments"/>; the caller
/// decides how often, because an observer may read a database.
/// </para>
/// </remarks>
public sealed class MeterSeriesCounter : IDisposable
{
    /// <summary>The most distinct tag sets kept per instrument before the counter stops adding and says so.</summary>
    public const int SeriesCapPerInstrument = 2_000_000;

    private readonly MeterListener _listener = new();
    private readonly ConcurrentDictionary<string, InstrumentSeries> _series = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, UpDownTotal> _upDown = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _publications = new(StringComparer.Ordinal);

    private MeterSeriesCounter()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            // One call per instrument OBJECT: a process that makes its meters once is told about each of them once,
            // and one that makes a meter per run is told again for every run. The series count cannot see that, because
            // two meters with the same name and the same tags are one series.
            _publications.AddOrUpdate(instrument.Meter.Name + "/" + instrument.Name, 1, static (_, count) => count + 1);
            listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<byte>((i, v, t, _) => Record(i, v, t));
        _listener.SetMeasurementEventCallback<short>((i, v, t, _) => Record(i, v, t));
        _listener.SetMeasurementEventCallback<int>((i, v, t, _) => Record(i, v, t));
        _listener.SetMeasurementEventCallback<long>((i, v, t, _) => Record(i, v, t));
        _listener.SetMeasurementEventCallback<float>((i, v, t, _) => Record(i, v, t));
        _listener.SetMeasurementEventCallback<double>((i, v, t, _) => Record(i, v, t));
        _listener.SetMeasurementEventCallback<decimal>((i, v, t, _) => Record(i, (double)v, t));
    }

    /// <summary>Starts listening to every meter, including the ones that already exist.</summary>
    /// <returns>The running counter.</returns>
    public static MeterSeriesCounter Start()
    {
        var counter = new MeterSeriesCounter();
        counter._listener.Start();
        return counter;
    }

    /// <summary>How many instruments have published at least one measurement.</summary>
    public int Instruments => _series.Count;

    /// <summary>The total number of distinct (instrument, tag set) pairs seen.</summary>
    public long SeriesTotal
    {
        get
        {
            long total = 0;

            foreach (var series in _series.Values)
            {
                total += series.Count;
            }

            return total;
        }
    }

    /// <summary>How many times any instrument has been announced to the listener, repeats included.</summary>
    public long Publications => _publications.Values.Sum();

    /// <summary>The instruments that were announced more than once, with how many times: a meter made again and again.</summary>
    /// <returns>A snapshot keyed by meter and instrument name.</returns>
    public Dictionary<string, long> RepeatedPublications()
        => _publications.Where(static pair => pair.Value > 1).ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);

    /// <summary>Whether any instrument reached <see cref="SeriesCapPerInstrument"/>.</summary>
    public bool Saturated => _series.Values.Any(static s => s.Saturated);

    /// <summary>Distinct tag sets per instrument.</summary>
    /// <returns>A snapshot keyed by instrument name, with the meter that owns it.</returns>
    public Dictionary<string, long> SeriesByInstrument()
    {
        var snapshot = new Dictionary<string, long>(_series.Count, StringComparer.Ordinal);

        foreach (var (name, series) in _series)
        {
            snapshot[name] = series.Count;
        }

        return snapshot;
    }

    /// <summary>The running sum of every up-down counter, which is its current value.</summary>
    /// <returns>A snapshot keyed by instrument name.</returns>
    public Dictionary<string, double> UpDownTotals()
    {
        var snapshot = new Dictionary<string, double>(_upDown.Count, StringComparer.Ordinal);

        foreach (var (name, total) in _upDown)
        {
            snapshot[name] = total.Read();
        }

        return snapshot;
    }

    /// <summary>Asks every observable instrument to publish its current measurements.</summary>
    public void Observe() => _listener.RecordObservableInstruments();

    /// <inheritdoc />
    public void Dispose() => _listener.Dispose();

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var name = instrument.Meter.Name + "/" + instrument.Name;
        var series = _series.GetOrAdd(name, static _ => new InstrumentSeries());
        series.Add(HashTags(tags));

        if (IsUpDown(instrument))
        {
            _upDown.GetOrAdd(name, static _ => new UpDownTotal()).Add(value);
        }
    }

    private static bool IsUpDown(Instrument instrument)
    {
        var type = instrument.GetType();

        // Only the synchronous kind reports deltas, so only its sum is a current
        // value. An observable up-down counter reports the value itself and
        // summing those would count every observation again.
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(UpDownCounter<>);
    }

    // Order independent: two call sites that list the same tags in a different
    // order are one series. Each pair is mixed on its own and the results are
    // added, so the order of the additions cannot matter.
    private static ulong HashTags(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        ulong sum = 0;

        foreach (var tag in tags)
        {
            var key = (ulong)(uint)StringComparer.Ordinal.GetHashCode(tag.Key);
            var value = tag.Value switch
            {
                null => 0UL,
                string text => (ulong)(uint)StringComparer.Ordinal.GetHashCode(text),
                _ => (ulong)(uint)(tag.Value.ToString() ?? string.Empty).GetHashCode(StringComparison.Ordinal),
            };

            sum += Mix((key << 32) ^ value);
        }

        return Mix(sum + (ulong)tags.Length);
    }

    // SplitMix64 finalizer.
    private static ulong Mix(ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }

    private sealed class InstrumentSeries
    {
        private readonly HashSet<ulong> _seen = [];
        private readonly Lock _gate = new();

        public bool Saturated { get; private set; }

        public long Count
        {
            get
            {
                lock (_gate)
                {
                    return _seen.Count;
                }
            }
        }

        public void Add(ulong hash)
        {
            lock (_gate)
            {
                if (_seen.Count >= SeriesCapPerInstrument)
                {
                    Saturated = true;
                    return;
                }

                _seen.Add(hash);
            }
        }
    }

    private sealed class UpDownTotal
    {
        private readonly Lock _gate = new();
        private double _sum;

        public void Add(double value)
        {
            lock (_gate)
            {
                _sum += value;
            }
        }

        public double Read()
        {
            lock (_gate)
            {
                return _sum;
            }
        }
    }
}
