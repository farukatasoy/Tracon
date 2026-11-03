using System.Collections;
using Microsoft.Extensions.Logging;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Collects what a log exporter is handed, which is more than the formatted line: the structured state of the record,
/// the scopes around it, the event id, and for a failure the type, message and full text of every exception in the chain.
/// </summary>
/// <remarks>
/// <para>
/// An exporter that ships logs to a collector (the OTLP log exporter is the common one) sends each record's attributes
/// and the exception's type, message and stack trace; the stack trace of an exception is its <c>ToString()</c>, which
/// carries every inner exception. The recording provider the test host registers keeps the line and the exception text
/// only, so a value that reaches an exporter through state or a scope would pass it unseen. This provider keeps what an
/// exporter would send, without taking the exporter package into the test project.
/// </para>
/// <para>
/// It stands in for the exporter: the bytes of the real one are not read. What it proves is that no record handed to the
/// logging pipeline, in any of its parts, carries the value.
/// </para>
/// </remarks>
internal sealed class StructuredLogProbe : ILoggerProvider, ISupportExternalScope
{
    private readonly List<string> _records = [];
    private readonly List<string> _chains = [];
    private IExternalScopeProvider? _scopes;

    /// <summary>Gets every record handed to the pipeline, in the order written.</summary>
    public string Text
    {
        get
        {
            lock (_records)
            {
                return string.Join('\n', _records);
            }
        }
    }

    /// <summary>
    /// Gets the type, message and data of every exception in the inner chain of every record, the way a writer that walks
    /// <see cref="Exception.InnerException"/> and sends each one's message reads it (an application performance monitor
    /// does). An exporter that sends the outermost message and the text of the exception does not read this.
    /// </summary>
    public string ChainText
    {
        get
        {
            lock (_chains)
            {
                return string.Join('\n', _chains);
            }
        }
    }

    /// <summary>Gets the number of records collected.</summary>
    public int Count
    {
        get
        {
            lock (_records)
            {
                return _records.Count;
            }
        }
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    /// <inheritdoc />
    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    /// <inheritdoc />
    public void Dispose()
    {
        // The records live as long as the test does.
    }

    private static void Describe(object? state, List<string> into, string prefix)
    {
        switch (state)
        {
            case null:
                return;
            case IEnumerable<KeyValuePair<string, object?>> pairs:
                into.AddRange(pairs.Select(pair => $"{prefix}{pair.Key}={pair.Value}"));

                return;
            case string text:
                into.Add(prefix + text);

                return;
            case IEnumerable items:
                into.Add(prefix + string.Join(',', items.Cast<object?>()));

                return;
            default:
                into.Add(prefix + state);

                return;
        }
    }

    private sealed class Logger(StructuredLogProbe owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => owner._scopes?.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            var lines = new List<string> { $"{logLevel} {category} event={eventId.Id}/{eventId.Name} body={formatter(state, exception)}" };

            Describe(state, lines, "attribute ");
            owner._scopes?.ForEachScope((scope, list) => Describe(scope, list, "scope "), lines);

            if (exception is not null)
            {
                lines.Add($"exception.type={exception.GetType().FullName} exception.message={exception.Message}");
                lines.Add("exception.stacktrace=" + exception);

                var chain = new List<string>();

                for (var current = exception; current is not null; current = current.InnerException)
                {
                    chain.Add($"{category} exception.type={current.GetType().FullName} exception.message={current.Message}");
                    chain.AddRange(current.Data.Cast<DictionaryEntry>().Select(static entry => $"exception.data {entry.Key}={entry.Value}"));
                }

                lock (owner._chains)
                {
                    owner._chains.AddRange(chain);
                }
            }

            lock (owner._records)
            {
                owner._records.Add(string.Join('\n', lines));
            }
        }
    }
}
