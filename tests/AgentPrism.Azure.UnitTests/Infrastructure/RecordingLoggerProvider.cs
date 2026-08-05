using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace AgentPrism.Azure.UnitTests.Infrastructure;

/// <summary>
/// Yazilan tum gunluk satirlarini bellekte toplayan gunlukleyici saglayicisi.
/// Sir sizinti testleri gunluge ne yazildigini bunun uzerinden denetler.
/// </summary>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IEnumerable<string> Entries => _entries;

    public string AllText => string.Join('\n', _entries);

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, _entries);

    public void Dispose()
    {
        // Toplanan satirlar test suresince kalir; serbest birakilacak kaynak yok.
    }

    private sealed class RecordingLogger(string category, ConcurrentQueue<string> entries) : ILogger
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

            entries.Enqueue($"{logLevel} {category} {formatter(state, exception)} {exception}");
        }
    }
}
