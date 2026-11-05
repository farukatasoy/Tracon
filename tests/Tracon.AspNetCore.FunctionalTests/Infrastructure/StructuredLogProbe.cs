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
/// A writer that does not stop at the outermost exception walks the graph and sends the type, message, stack trace and
/// data of each frame (an error tracker and a structured exception formatter do). Every record therefore also carries
/// that walk, <see cref="AggregateException"/> branches included, so a value that sits in an inner frame cannot pass
/// unseen behind a masked outer message.
/// </para>
/// <para>
/// It stands in for the exporter: the bytes of the real one are not read. What it proves is that no record handed to the
/// logging pipeline, in any of its parts, carries the value.
/// </para>
/// </remarks>
internal sealed class StructuredLogProbe : ILoggerProvider, ISupportExternalScope
{
    private readonly List<string> _records = [];
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

    // The graph as a property-by-property writer sends it: each frame's type, message, stack trace and data.
    private static IEnumerable<string> Walk(Exception exception, string category)
    {
        var pending = new Stack<Exception>();

        pending.Push(exception);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            yield return $"{category} frame.type={current.GetType().FullName} frame.message={current.Message} frame.stacktrace={current.StackTrace}";

            foreach (var entry in current.Data.Cast<DictionaryEntry>())
            {
                yield return $"frame.data {entry.Key}={entry.Value}";
            }

            if (current is AggregateException aggregate)
            {
                foreach (var branch in aggregate.InnerExceptions.Reverse())
                {
                    pending.Push(branch);
                }
            }
            else if (current.InnerException is { } inner)
            {
                pending.Push(inner);
            }
        }
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
                lines.AddRange(Walk(exception, category));
            }

            lock (owner._records)
            {
                owner._records.Add(string.Join('\n', lines));
            }
        }
    }
}
