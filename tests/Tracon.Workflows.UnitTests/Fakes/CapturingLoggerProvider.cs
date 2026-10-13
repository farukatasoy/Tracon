using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Tracon.Workflows.UnitTests.Fakes;

/// <summary>Keeps every log entry written through it, for a test to read back.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    /// <summary>Gets the entries written so far, in order.</summary>
    public IReadOnlyList<LogEntry> Entries => [.. _entries];

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <summary>One log entry.</summary>
    internal sealed record LogEntry(string Category, LogLevel Level, string Message, Exception? Exception);

    private sealed class CapturingLogger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => entries.Enqueue(new LogEntry(category, logLevel, formatter(state, exception), exception));
    }
}
