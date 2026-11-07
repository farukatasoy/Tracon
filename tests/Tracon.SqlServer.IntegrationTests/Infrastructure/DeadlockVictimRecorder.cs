using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace Tracon.SqlServer.IntegrationTests.Infrastructure;

/// <summary>
/// Counts the commands of one schema that the server ended as a deadlock victim, including the ones the store sent again.
/// </summary>
/// <remarks>
/// <para>
/// The store sends a statement it lost to a deadlock once more, so a caller never sees the victim and a test that only
/// watches the result cannot tell a run with deadlocks from one without. The SQL client reports every failed command to the
/// <c>SqlClientDiagnosticListener</c> source before the store decides what to do, and this type listens there.
/// </para>
/// <para>
/// A command counts only when its text names the schema, so the deliberate deadlocks of the other test classes, which run in
/// the same server at the same time, are not counted.
/// </para>
/// </remarks>
internal sealed class DeadlockVictimRecorder : IDisposable
{
    private const string ListenerName = "SqlClientDiagnosticListener";

    private const string CommandErrorEvent = "Microsoft.Data.SqlClient.WriteCommandError";

    private const int DeadlockVictimNumber = 1205;

    private readonly string _schemaMarker;

    private readonly ConcurrentQueue<string> _victims = new();

    private readonly ConcurrentBag<IDisposable> _subscriptions = [];

    private readonly IDisposable _allListeners;

    private int _disposed;

    private DeadlockVictimRecorder(string schemaName)
    {
        _schemaMarker = string.Create(CultureInfo.InvariantCulture, $"{schemaName}.");
        _allListeners = DiagnosticListener.AllListeners.Subscribe(new ListenerObserver(this));
    }

    /// <summary>Gets the command texts that were chosen as a victim, first line only.</summary>
    public IReadOnlyCollection<string> Victims => _victims;

    /// <summary>Starts counting the victims of <paramref name="schemaName"/>.</summary>
    /// <param name="schemaName">The schema whose commands are counted.</param>
    /// <returns>The recorder; dispose it to stop.</returns>
    public static DeadlockVictimRecorder Start(string schemaName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);

        return new DeadlockVictimRecorder(schemaName);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _allListeners.Dispose();

        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }
    }

    private void Record(object? payload)
    {
        if (payload is null)
        {
            return;
        }

        var type = payload.GetType();

        if (type.GetProperty("Exception")?.GetValue(payload) is not SqlException { Number: DeadlockVictimNumber }
            || type.GetProperty("Command")?.GetValue(payload) is not SqlCommand command
            || !command.CommandText.Contains(_schemaMarker, StringComparison.Ordinal))
        {
            return;
        }

        var text = command.CommandText.AsSpan().Trim();
        var firstLine = text.IndexOf('\n');

        _victims.Enqueue((firstLine < 0 ? text : text[..firstLine]).Trim().ToString());
    }

    private sealed class ListenerObserver(DeadlockVictimRecorder owner) : IObserver<DiagnosticListener>
    {
        public void OnNext(DiagnosticListener value)
        {
            if (string.Equals(value.Name, ListenerName, StringComparison.Ordinal))
            {
                owner._subscriptions.Add(value.Subscribe(new EventObserver(owner)));
            }
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }

    private sealed class EventObserver(DeadlockVictimRecorder owner) : IObserver<KeyValuePair<string, object?>>
    {
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (string.Equals(value.Key, CommandErrorEvent, StringComparison.Ordinal))
            {
                owner.Record(value.Value);
            }
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }
}
