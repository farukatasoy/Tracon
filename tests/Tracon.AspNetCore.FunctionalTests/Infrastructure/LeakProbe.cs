using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Collects what a process hands to its telemetry while a test runs: every span name, tag, baggage item and span event,
/// and every metric tag. A secret search over these two covers what the response and the log do not.
/// </summary>
/// <remarks>
/// The listeners are process-wide, so activities and measurements of tests running at the same time are collected
/// too. That cannot cause a false hit: the searches are for values only the test itself holds.
/// </remarks>
internal sealed class LeakProbe : IDisposable
{
    private readonly List<string> _spans = [];
    private readonly List<string> _measurements = [];
    private readonly ActivityListener _activities;
    private readonly MeterListener _meters;

    /// <summary>Starts listening to every activity source and every meter.</summary>
    public LeakProbe()
    {
        _activities = new ActivityListener
        {
            ShouldListenTo = static _ => true,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (_spans)
                {
                    _spans.Add(activity.DisplayName);
                    _spans.AddRange(activity.TagObjects.Select(static tag => $"{tag.Key}={tag.Value}"));
                    _spans.AddRange(activity.Baggage.Select(static item => $"{item.Key}={item.Value}"));
                    _spans.AddRange(activity.Events.SelectMany(static activityEvent => activityEvent.Tags.Select(static tag => $"{tag.Key}={tag.Value}")));
                }
            },
        };

        ActivitySource.AddActivityListener(_activities);

        _meters = new MeterListener
        {
            InstrumentPublished = static (instrument, listener) => listener.EnableMeasurementEvents(instrument),
        };

        _meters.SetMeasurementEventCallback<long>((_, _, tags, _) => Record(tags));
        _meters.SetMeasurementEventCallback<double>((_, _, tags, _) => Record(tags));
        _meters.SetMeasurementEventCallback<int>((_, _, tags, _) => Record(tags));
        _meters.Start();
    }

    /// <summary>Gets every span name, tag, baggage item and span event collected so far, one per line.</summary>
    public string Spans
    {
        get
        {
            lock (_spans)
            {
                return string.Join('\n', _spans);
            }
        }
    }

    /// <summary>Gets every metric tag collected so far, one per line.</summary>
    public string Measurements
    {
        get
        {
            lock (_measurements)
            {
                return string.Join('\n', _measurements);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _activities.Dispose();
        _meters.Dispose();
    }

    private void Record(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        lock (_measurements)
        {
            foreach (var tag in tags)
            {
                _measurements.Add($"{tag.Key}={tag.Value}");
            }
        }
    }
}
