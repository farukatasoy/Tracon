using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Applies pending migrations at application startup and ensures the default
/// tenant record exists.
/// </summary>
/// <remarks>
/// <para>
/// If <c>AutoApplyMigrations</c> is off, no migration is applied. In that case
/// making sure the schema is ready is the consumer's responsibility: a
/// deployment step runs <c>tracon migrate</c> or calls
/// <see cref="IMigrationApplier.ApplyAsync"/>. Start reads the migration ledger,
/// writes nothing, and logs a warning when migrations are pending or the database
/// cannot be reached; the host starts either way.
/// </para>
/// <para>
/// <strong>A failure does not start the application.</strong> A Tracon
/// instance that runs while the schema is not ready silently loses data, so
/// migration failures are not swallowed. The observability rule (a store
/// failure does not stop a run) applies only to run recording, not to schema
/// setup.
/// </para>
/// <para>
/// This class is shared across every SQL provider; everything
/// provider-specific comes through <see cref="SqlStoreContext"/>.
/// </para>
/// </remarks>
internal sealed class MigrationHostedService : IHostedService
{
    /// <summary>How many pending migration names the warning lists.</summary>
    private const int FirstPendingShown = 3;

    /// <summary>How long start waits for the ledger of a host that applies no migration itself.</summary>
    private static readonly TimeSpan VerificationTimeout = TimeSpan.FromSeconds(10);

    private readonly MigrationRunner _runner;
    private readonly SqlStoreContext _storeContext;
    private readonly TraconOptions _traconOptions;
    private readonly IEnumerable<SqlPersistenceRegistrationMarker> _registrations;
    private readonly SchemaReadyGate _schemaReadyGate;
    private readonly ILogger<MigrationHostedService> _logger;

    /// <summary>Creates a new startup service.</summary>
    /// <param name="runner">The migration runner.</param>
    /// <param name="storeContext">The store context.</param>
    /// <param name="traconOptions">The Tracon options.</param>
    /// <param name="registrations">The registered persistence providers.</param>
    /// <param name="schemaReadyGate">The gate that holds back background services that touch SQL.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public MigrationHostedService(
        MigrationRunner runner,
        SqlStoreContext storeContext,
        IOptions<TraconOptions> traconOptions,
        IEnumerable<SqlPersistenceRegistrationMarker> registrations,
        SchemaReadyGate schemaReadyGate,
        ILogger<MigrationHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(storeContext);
        ArgumentNullException.ThrowIfNull(traconOptions);
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(schemaReadyGate);
        ArgumentNullException.ThrowIfNull(logger);

        _runner = runner;
        _storeContext = storeContext;
        _traconOptions = traconOptions.Value;
        _registrations = registrations;
        _schemaReadyGate = schemaReadyGate;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!IsWinningProvider())
        {
            // 🚨 If more than one persistence provider is registered,
            // WarnOnMultipleProviders() has already warned BEFORE this point.
            // Sql.Shared is compiled SEPARATELY for every provider (this file
            // is copied by a link) — meaning SqlStoreContext/MigrationRunner/
            // MigrationHostedService is a DIFFERENT CLR type in EVERY provider.
            // `services.Replace(...)` therefore replaces only the SAME provider's
            // own type; it does NOT REMOVE a competing provider's registration.
            // Without this guard, the losing provider would also SILENTLY write
            // its own schema/default tenant (measured: both databases
            // ended up with a schema). Only the winning (last registered)
            // provider applies migrations; the others leave the gate untouched
            // and exit.
            //
            // 🚨 The LOSER does not open the gate. The gate is ONE shared signal;
            // if the loser opened it immediately, it would open before the
            // winner's migration finished, and waiting background services
            // would query an empty schema. Measured (the PostgreSQL
            // schema is already current, SQLite wins; the losing PostgreSQL
            // provider opens the gate instantly and hits "no such table").
            // The winner calls MarkReady on EVERY path - even when
            // AutoApplyMigrations is off - so the gate never stays closed forever.
            WarnOnMultipleProviders();

            return;
        }

        WarnOnMultipleProviders();

        if (!_storeContext.AutoApplyMigrations)
        {
            // The gate OPENS first: the schema is the consumer's responsibility, and
            // there is no benefit in holding background services waiting forever. The
            // check below reads the ledger and writes nothing, so it can run beside them.
            _schemaReadyGate.MarkReady();

            await ReportSchemaStateAsync(cancellationToken).ConfigureAwait(false);

            return;
        }

        await _runner.ApplyAsync(cancellationToken).ConfigureAwait(false);
        await EnsureDefaultTenantAsync(cancellationToken).ConfigureAwait(false);

        // 🚨 The gate opens ONLY HERE, after both the migration and the
        // default tenant write have completed. If the migration fails, the
        // gate stays closed; the host does not start anyway and waiting
        // services exit through the stoppingToken.
        _schemaReadyGate.MarkReady();
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Tells the operator whether the schema a host that applies nothing itself is about to run on is current.
    /// </summary>
    /// <param name="cancellationToken">The host's start-up token.</param>
    /// <returns>A task that completes when the state has been logged.</returns>
    /// <remarks>
    /// <para>
    /// Without this the host starts without a word and the first run fails on a missing table, behind a run-recording
    /// warning the run itself survives. A pending migration and an unreachable database are warnings; a current schema
    /// stays at information.
    /// </para>
    /// <para>
    /// 🚨 Nothing here stops the host. A deployment that applies the schema in its own step must not be held back by a
    /// database that is slow to answer, so the read is bounded and a failure of it is a warning too.
    /// </para>
    /// </remarks>
    private async ValueTask ReportSchemaStateAsync(CancellationToken cancellationToken)
    {
        using var verification = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        verification.CancelAfter(VerificationTimeout);

        SqlPersistenceDiagnosticsSnapshot snapshot;

        try
        {
            snapshot = await _runner.GetSnapshotAsync(verification.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            _logger.LogWarning(
                exception,
                "Tracon migrations are not applied automatically (AutoApplyMigrations is off) and the schema could not " +
                "be verified within {Seconds} seconds; the host starts anyway. Keeping the schema current is the " +
                "caller's responsibility.",
                VerificationTimeout.TotalSeconds);

            return;
        }

        if (!snapshot.CanConnect)
        {
            _logger.LogWarning(
                "Tracon migrations are not applied automatically (AutoApplyMigrations is off) and the database could " +
                "not be reached, so the schema was not verified. Run `tracon migrate` or " +
                "IMigrationApplier.ApplyAsync before this instance takes traffic.");

            return;
        }

        if (snapshot.PendingMigrations.Count > 0)
        {
            _logger.LogWarning(
                "Tracon migrations are not applied automatically (AutoApplyMigrations is off) and {PendingCount} " +
                "migration(s) are pending, starting with {FirstPending}. Runs and other writes fail until the schema " +
                "is current: run `tracon migrate` or IMigrationApplier.ApplyAsync before this instance takes traffic.",
                snapshot.PendingMigrations.Count,
                string.Join(", ", snapshot.PendingMigrations.Take(FirstPendingShown)));

            return;
        }

        _logger.LogInformation(
            "Tracon migrations are not applied automatically (AutoApplyMigrations is off). " +
            "Keeping the schema current is the caller's responsibility.");
    }

    /// <summary>
    /// Warns when more than one persistence provider is registered.
    /// </summary>
    /// <remarks>
    /// If <c>UsePostgreSql()</c> and <c>UseSqlServer()</c> are called in the
    /// same chain, <em>the last registration wins</em> and which database the
    /// data goes to depends on call order. This is a configuration mistake.
    /// The registration is not blocked — it could be a deliberate migration
    /// scenario — but it is not silent either.
    /// </remarks>
    private void WarnOnMultipleProviders()
    {
        var names = _registrations.Select(static registration => registration.ProviderName).ToArray();

        if (names.Length <= 1)
        {
            return;
        }

        _logger.LogWarning(
            "Tracon has more than one persistence provider registered: {Providers}. " +
            "The last registration wins and {Winner} is currently in use. Call only one.",
            string.Join(", ", names),
            WinningProviderName());
    }

    /// <summary>
    /// Whether the provider this instance is bound to is the winner under the
    /// "last registration wins" rule.
    /// </summary>
    /// <remarks>
    /// <see cref="_registrations"/> is a shared (<c>Tracon.Abstractions</c>)
    /// type, so it sees markers from ALL providers and preserves registration
    /// order; the last element is the "last called" provider. <see cref="_storeContext"/>,
    /// on the other hand, is SPECIFIC to this assembly (Sql.Shared is compiled
    /// separately per provider) — it can only answer "am I the winner" by
    /// comparing its own name against the last name in the shared list.
    /// </remarks>
    private bool IsWinningProvider()
    {
        var names = _registrations.Select(static registration => registration.ProviderName).ToArray();

        return names.Length == 0
            || string.Equals(names[^1], _storeContext.ProviderName, StringComparison.Ordinal);
    }

    private string WinningProviderName()
        => _registrations.Select(static registration => registration.ProviderName).LastOrDefault()
            ?? _storeContext.ProviderName;

    private async ValueTask EnsureDefaultTenantAsync(CancellationToken cancellationToken)
    {
        var tenantId = _traconOptions.DefaultTenantId;

        await _storeContext.ExecuteUpsertAsync(
            _storeContext.Sql.UpsertTenant,
            command =>
            {
                DbHelpers.Add(command, "id", TraconId.NewId());
                DbHelpers.Add(command, "slug", tenantId);
                DbHelpers.Add(command, "display_name", tenantId);
                _storeContext.Dialect.AddTimestamp(command, "created_at", DateTimeOffset.UtcNow);
            },
            (command, token) => DbHelpers.ExecuteAsync(command, token),
            cancellationToken).ConfigureAwait(false);
    }
}
