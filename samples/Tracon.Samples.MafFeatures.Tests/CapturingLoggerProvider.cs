using Microsoft.Extensions.Logging;

namespace Tracon.Samples.MafFeatures.Tests;

/// <summary>Keeps the warnings a host writes, so a test can read what Tracon told the consumer.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<string> _warnings = [];

    /// <summary>One entry per warning: the category, then the formatted message.</summary>
    public IReadOnlyList<string> Warnings
    {
        get
        {
            lock (_warnings)
            {
                return [.. _warnings];
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose()
    {
        // Nothing to release.
    }

    private void Add(string entry)
    {
        lock (_warnings)
        {
            _warnings.Add(entry);
        }
    }

    private sealed class CapturingLogger(CapturingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                owner.Add($"{category} {formatter(state, exception)}");
            }
        }
    }
}
