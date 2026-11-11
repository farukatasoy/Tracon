using System.Globalization;

namespace Tracon.Tests.Common;

/// <summary>
/// Measures how late a heartbeat arrives and how long a thread pool work item waited, and keeps what it measured.
/// </summary>
/// <remarks>
/// <para>
/// A test that waited 30 s for a condition says what never happened. It does not say whether the test process itself stood
/// still: a paused process (a CPU-starved runner, a stop-the-world collection, a page-in storm) delays every timer at once and
/// makes two real timers race in an order nobody wrote down. The recorder is the instrument that separates the two readings.
/// </para>
/// <para>
/// A dedicated thread, not a pool thread, calls <see cref="Beat"/> every <see cref="Interval"/>. The time since the last call,
/// minus the interval, is how long the process was unable to run that thread. Each beat also queues a work item on the thread
/// pool; the time that item waited is how long the pool was unable to start new work. Both numbers are kept in a ring so a
/// failing wait can ask for the worst values inside its own window, and every value at or above the threshold is written to
/// the sink with its time.
/// </para>
/// <para>
/// The clock and the sink are injected: a test advances a <see cref="TimeProvider"/> by hand and reads the sink, and never
/// waits for real time. Only <see cref="Beat"/> and <see cref="Flush"/> write to the sink, and only the dedicated thread calls
/// them, so the sink needs no lock.
/// </para>
/// </remarks>
internal sealed class PulseRecorder
{
    /// <summary>How many measurements the ring keeps; about seven minutes at the default interval.</summary>
    internal const int RingCapacity = 4096;

    /// <summary>The most lines the sink is given, so a process that stalls all the time cannot fill a disk.</summary>
    internal const int SinkLineLimit = 500;

    private readonly TimeProvider _clock;
    private readonly Action<string> _sink;
    private readonly Lock _gate = new();
    private readonly Sample[] _ring = new Sample[RingCapacity];
    private readonly List<(long Taken, TimeSpan Waited)> _poolArrivals = [];
    private readonly Dictionary<long, Outstanding> _outstanding = [];
    private long _nextProbe;
    private long _lastBeat;
    private int _count;
    private int _written;
    private bool _waitingReported;

    /// <summary>Creates a recorder.</summary>
    /// <param name="clock">The clock the beats are measured on.</param>
    /// <param name="interval">How often <see cref="Beat"/> is called.</param>
    /// <param name="threshold">The delay at or above which a measurement is written to <paramref name="sink"/>.</param>
    /// <param name="sink">Receives one line per delay at or above the threshold.</param>
    public PulseRecorder(TimeProvider clock, TimeSpan interval, TimeSpan threshold, Action<string> sink)
    {
        _clock = clock;
        Interval = interval;
        Threshold = threshold;
        _sink = sink;
        _lastBeat = clock.GetTimestamp();
    }

    /// <summary>How often <see cref="Beat"/> is meant to be called.</summary>
    public TimeSpan Interval { get; }

    /// <summary>The delay at or above which a measurement is written to the sink.</summary>
    public TimeSpan Threshold { get; }

    /// <summary>The timestamp on the recorder's clock, the same one <see cref="Window"/> takes.</summary>
    public long Now => _clock.GetTimestamp();

    /// <summary>
    /// Records one heartbeat: how late this call is, and writes the pool delays that arrived since the last one.
    /// </summary>
    /// <remarks>Call it from one dedicated thread, every <see cref="Interval"/>.</remarks>
    public void Beat()
    {
        var now = _clock.GetTimestamp();
        var late = _clock.GetElapsedTime(_lastBeat, now) - Interval;
        _lastBeat = now;

        if (late < TimeSpan.Zero)
        {
            late = TimeSpan.Zero;
        }

        Add(new Sample(now, late, TimeSpan.Zero));

        if (late >= Threshold)
        {
            Write($"{Stamp()} process stall {Milliseconds(late)} ms (a heartbeat thread that was due every {Milliseconds(Interval)} ms woke late)");
        }

        Flush();
    }

    /// <summary>
    /// Notes that a work item is about to be queued on the thread pool.
    /// </summary>
    /// <returns>The probe's number; pass it to <see cref="PoolProbeStarted"/> from the work item.</returns>
    public long PoolProbeQueued()
    {
        lock (_gate)
        {
            var id = ++_nextProbe;
            _outstanding[id] = new Outstanding(_clock.GetTimestamp(), Reported: false);

            return id;
        }
    }

    /// <summary>
    /// Records that the work item of probe <paramref name="id"/> has started, so how long it waited is known.
    /// </summary>
    /// <param name="id">The value <see cref="PoolProbeQueued"/> returned. May be called from any thread.</param>
    public void PoolProbeStarted(long id)
    {
        lock (_gate)
        {
            if (!_outstanding.Remove(id, out var probe))
            {
                return;
            }

            var now = _clock.GetTimestamp();
            _poolArrivals.Add((now, _clock.GetElapsedTime(probe.Queued, now)));
        }
    }

    /// <summary>
    /// Moves the pool delays that arrived since the last call into the ring and writes the ones above the threshold,
    /// including a work item that is still waiting.
    /// </summary>
    public void Flush()
    {
        (long Taken, TimeSpan Waited)[] arrived;
        var stillWaiting = TimeSpan.Zero;

        lock (_gate)
        {
            arrived = [.. _poolArrivals];
            _poolArrivals.Clear();

            var now = _clock.GetTimestamp();

            foreach (var (id, probe) in _outstanding.ToArray())
            {
                var waited = _clock.GetElapsedTime(probe.Queued, now);

                if (!probe.Reported && waited >= Threshold)
                {
                    _outstanding[id] = probe with { Reported = true };
                    stillWaiting = waited > stillWaiting ? waited : stillWaiting;
                }
            }
        }

        // One line per episode: a starved pool delays every probe queued meanwhile, and ten lines for one stall would say
        // the same thing ten times.
        if (stillWaiting > TimeSpan.Zero && !_waitingReported)
        {
            _waitingReported = true;
            Write($"{Stamp()} thread pool delay at least {Milliseconds(stillWaiting)} ms (a work item is still waiting for a pool thread)");
        }

        var late = 0;
        var longest = TimeSpan.Zero;

        foreach (var (taken, waited) in arrived)
        {
            Add(new Sample(taken, TimeSpan.Zero, waited));

            if (waited >= Threshold)
            {
                late++;
                longest = waited > longest ? waited : longest;
            }
            else
            {
                _waitingReported = false;
            }
        }

        if (late > 0)
        {
            Write($"{Stamp()} thread pool delay {Milliseconds(longest)} ms ({late} work item{(late == 1 ? string.Empty : "s")} waited at least {Milliseconds(Threshold)} ms before a pool thread started them; the longest is shown)");
            _waitingReported = false;
        }
    }

    /// <summary>
    /// The worst delays recorded since <paramref name="since"/>.
    /// </summary>
    /// <param name="since">A value of <see cref="Now"/>.</param>
    /// <returns>The longest process stall, the longest thread pool delay and how many measurements were inside the window.</returns>
    public PulseWindow Window(long since)
    {
        var stall = TimeSpan.Zero;
        var pool = TimeSpan.Zero;
        var seen = 0;

        lock (_gate)
        {
            var stored = Math.Min(_count, RingCapacity);

            for (var index = 0; index < stored; index++)
            {
                var sample = _ring[index];

                if (sample.Taken < since)
                {
                    continue;
                }

                seen++;
                stall = sample.Stall > stall ? sample.Stall : stall;
                pool = sample.PoolDelay > pool ? sample.PoolDelay : pool;
            }

            // A work item that has not started yet is the worst case and has no sample: the pool may never recover.
            var now = _clock.GetTimestamp();

            foreach (var probe in _outstanding.Values)
            {
                var waiting = _clock.GetElapsedTime(Math.Max(probe.Queued, since), now);
                pool = waiting > pool ? waiting : pool;
            }
        }

        return new PulseWindow(stall, pool, seen);
    }

    private static string Milliseconds(TimeSpan value) => ((long)value.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);

    private void Add(Sample sample)
    {
        lock (_gate)
        {
            _ring[_count % RingCapacity] = sample;
            _count++;
        }
    }

    private string Stamp() => _clock.GetUtcNow().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private void Write(string line)
    {
        if (_written >= SinkLineLimit)
        {
            return;
        }

        _written++;
        _sink(_written == SinkLineLimit ? $"{line}{Environment.NewLine}(the line limit of {SinkLineLimit} was reached; later delays are not written)" : line);
    }

    private sealed record Outstanding(long Queued, bool Reported);

    private sealed record Sample(long Taken, TimeSpan Stall, TimeSpan PoolDelay);
}

/// <summary>What <see cref="PulseRecorder.Window"/> found.</summary>
/// <param name="LongestStall">The longest time the heartbeat thread could not run.</param>
/// <param name="LongestPoolDelay">The longest time a thread pool work item waited to start.</param>
/// <param name="Measurements">How many measurements fell inside the window; zero means the instrument had nothing to say.</param>
internal sealed record PulseWindow(TimeSpan LongestStall, TimeSpan LongestPoolDelay, int Measurements);
