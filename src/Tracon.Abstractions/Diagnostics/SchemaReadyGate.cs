namespace Tracon;

/// <summary>
/// Delays background services until the SQL schema is ready.
/// </summary>
/// <remarks>
/// <para>
/// The startup service that applies migrations is an <c>IHostedService</c> and waits
/// for them in <c>StartAsync</c>. However, <c>BackgroundService.StartAsync</c> returns
/// without waiting for <c>ExecuteAsync</c>. <c>IHostedService</c> instances start in
/// registration order. If the chain calls <c>.UseMcp()</c> before <c>.UseSqlite()</c>,
/// the background service can make its first SQL attempt before migrations complete and
/// receive "no such table".
/// </para>
/// <para>
/// The gate is <strong>independent of registration order</strong>. The waiting side
/// does not know the order and waits only for the ready signal. Enforcing an order
/// would be fragile because the consumer writes the chain and no ordering can cover
/// every configuration.
/// </para>
/// <para>
/// The gate <em>opens automatically</em> when no SQL persistence provider is
/// registered, such as for in-memory stores. Otherwise, background services would wait
/// forever in an in-memory installation.
/// </para>
/// </remarks>
public sealed class SchemaReadyGate
{
    private readonly TaskCompletionSource _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly IEnumerable<SqlPersistenceRegistrationMarker> _registrations;

    /// <summary>Initializes a new instance of the <see cref="SchemaReadyGate"/> class.</summary>
    /// <param name="registrations">
    /// The registered SQL persistence providers. If empty, the gate never closes.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="registrations"/> is <see langword="null"/>.
    /// </exception>
    public SchemaReadyGate(IEnumerable<SqlPersistenceRegistrationMarker> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        _registrations = registrations;
    }

    /// <summary>Gets whether the schema is ready.</summary>
    public bool IsReady => _ready.Task.IsCompleted;

    /// <summary>
    /// Marks the schema as ready. The migration startup service calls this method.
    /// </summary>
    /// <remarks>
    /// Repeated calls are harmless. This method also runs when
    /// <c>AutoApplyMigrations</c> is disabled. In that case the consumer is
    /// responsible for the schema, and delaying background services has no value.
    /// </remarks>
    public void MarkReady() => _ready.TrySetResult();

    /// <summary>
    /// Waits until the schema is ready.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token for the wait.</param>
    /// <returns>A task that completes when the schema is ready.</returns>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> is cancelled. If migration fails,
    /// the host already stops and the waiting service exits through that path.
    /// </exception>
    public Task WaitAsync(CancellationToken cancellationToken)
    {
        // Read registrations after the service provider is built. The complete
        // chain has run by then. Check each call until the gate opens; then the
        // completed task returns directly.
        if (!_ready.Task.IsCompleted && !_registrations.Any())
        {
            _ready.TrySetResult();
        }

        return _ready.Task.WaitAsync(cancellationToken);
    }
}
