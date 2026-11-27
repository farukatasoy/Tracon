using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Tracon.Core.UnitTests.Fakes;

/// <summary>
/// Collects the formatted warnings a container writes, for a test that composes the real host services and reads what an
/// operator would read.
/// </summary>
internal sealed class WarningLogProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _warnings = new();

    /// <summary>Gets every warning written so far, formatted as the operator sees it.</summary>
    public IReadOnlyList<string> Warnings => [.. _warnings];

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new WarningLogger(_warnings);

    /// <inheritdoc />
    public void Dispose()
    {
        // The collected lines belong to the test; there is nothing to release.
    }

    private sealed class WarningLogger(ConcurrentQueue<string> warnings) : ILogger
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
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (logLevel == LogLevel.Warning)
            {
                warnings.Enqueue(formatter(state, exception));
            }
        }
    }
}
