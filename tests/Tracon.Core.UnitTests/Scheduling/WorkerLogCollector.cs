using Microsoft.Extensions.Logging;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>Keeps every entry the job worker logs, with its level, its formatted text and its exception.</summary>
internal sealed class WorkerLogCollector : ILogger<JobWorkerBackgroundService>
{
    private readonly List<(LogLevel Level, string Message, Exception? Exception)> _entries = [];

    /// <summary>Gets a copy of the entries logged so far.</summary>
    public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_entries)
        {
            _entries.Add((logLevel, formatter(state, exception), exception));
        }
    }
}
