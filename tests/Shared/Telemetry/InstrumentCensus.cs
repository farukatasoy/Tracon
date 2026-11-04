using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Tracon.Tests.Common;

/// <summary>
/// Counts the telemetry instruments and activity sources that are made while it is open, and how many of them are
/// still alive.
/// </summary>
/// <remarks>
/// <para>
/// The chat client the Microsoft.Extensions.AI library builds for model-call telemetry makes its own
/// <see cref="Meter"/> and <see cref="ActivitySource"/> when it is constructed and frees them only when it is disposed.
/// Neither takes an <see cref="IMeterFactory"/> or a name from the caller, so a listener cannot be bound to the meter
/// of one test: it sees every meter in the process. The census therefore selects by the name of the instrument (one of
/// the four a chat client makes) and by the name of the activity source, never by the name of the meter, and a test
/// asserts an upper bound with room for the instruments other tests make at the same moment.
/// </para>
/// <para>
/// An instrument counts as <see cref="LiveInstruments"/> from the moment it is published until its meter is disposed
/// and the runtime reports its measurements complete. A process that makes a meter for every agent it compiles and
/// never disposes one shows a live count that grows with the compiles.
/// </para>
/// </remarks>
internal sealed class InstrumentCensus : IDisposable
{
    /// <summary>One of the four instruments a chat client's telemetry makes per instance; any one of them counts the clients.</summary>
    internal const string PerClientInstrument = "gen_ai.client.token.usage";

    private readonly MeterListener _meters = new();
    private readonly ActivityListener _sources;
    private readonly string _sourceName;
    private readonly ConcurrentDictionary<Instrument, byte> _live = new();
    private int _publishedInstruments;
    private int _createdSources;
    private volatile bool _counting;

    private InstrumentCensus(string sourceName)
    {
        _sourceName = sourceName;

        _meters.InstrumentPublished = (instrument, listener) =>
        {
            if (!_counting || !string.Equals(instrument.Name, PerClientInstrument, StringComparison.Ordinal))
            {
                return;
            }

            _live[instrument] = 0;
            Interlocked.Increment(ref _publishedInstruments);

            // A listener hears an instrument's measurements complete only for an instrument it enabled.
            listener.EnableMeasurementEvents(instrument);
        };
        _meters.MeasurementsCompleted = (instrument, state) => _live.TryRemove(instrument, out byte _);

        _sources = new ActivityListener
        {
            ShouldListenTo = source =>
            {
                if (_counting && string.Equals(source.Name, _sourceName, StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref _createdSources);
                }

                return false;
            },
        };

    }

    /// <summary>Starts counting.</summary>
    /// <param name="sourceName">The name of the activity sources to count.</param>
    /// <returns>The open census.</returns>
    internal static InstrumentCensus Open(string sourceName)
    {
        var census = new InstrumentCensus(sourceName);

        // Both calls announce what exists already; counting starts after them, so only what is made later is counted.
        census._meters.Start();
        ActivitySource.AddActivityListener(census._sources);
        census._counting = true;

        return census;
    }

    /// <summary>Gets the number of chat client instruments published since the census opened.</summary>
    internal int PublishedInstruments => Volatile.Read(ref _publishedInstruments);

    /// <summary>Gets the number of those instruments whose meter is still alive.</summary>
    internal int LiveInstruments => _live.Count;

    /// <summary>Gets the number of activity sources with the counted name made since the census opened.</summary>
    internal int CreatedSources => Volatile.Read(ref _createdSources);

    /// <inheritdoc />
    public void Dispose()
    {
        _meters.Dispose();
        _sources.Dispose();
    }
}
