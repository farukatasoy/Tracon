namespace Tracon.Ui.E2ETests.Infrastructure;

/// <summary>The events one page produced during a test, kept up to a limit; the events past it are counted, not forgotten.</summary>
/// <remarks>
/// A page in a loop can write thousands of requests. The first lines say how it started and the limit keeps the evidence
/// small, but a list that stopped without a word read as a page that went quiet: the summary now ends with the count of
/// what was left out.
/// </remarks>
/// <param name="limit">How many events are kept.</param>
internal sealed class EventLog(int limit)
{
    private readonly List<string> _lines = [];
    private int _dropped;

    /// <summary>Records an event, or counts it when the limit is reached.</summary>
    /// <param name="line">What happened.</param>
    public void Record(string line)
    {
        lock (_lines)
        {
            if (_lines.Count < limit)
            {
                _lines.Add(line);
            }
            else
            {
                _dropped++;
            }
        }
    }

    /// <summary>The kept events in order, then one line with the number that were dropped when any were.</summary>
    /// <returns>The lines of the summary.</returns>
    public IReadOnlyList<string> Lines()
    {
        lock (_lines)
        {
            return _dropped == 0
                ? [.. _lines]
                : [.. _lines, $"{_dropped} more events were dropped after the first {limit}"];
        }
    }
}
