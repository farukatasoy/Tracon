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
/// making sure the schema is ready is the consumer's responsibility;
/// <see cref="MigrationRunner"/> can be run as a separate deployment step.
/// </para>
/// <para>
/// <strong>A failure does not start the application.</strong> An Tracon
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
            // is copied by a link, K-176) — meaning SqlStoreContext/MigrationRunner/
            // MigrationHostedService is a DIFFERENT CLR type in EVERY provider.
            // `services.Replace(...)` therefore replaces only the SAME provider's
            // own type; it does NOT REMOVE a competing provider's registration.
            // Without this guard, the losing provider would also SILENTLY write
            // its own schema/default tenant (measured: MT-PKG-082, both databases
            // ended up with a schema). Only the winning (last registered)
            // provider applies migrations; the others leave the gate untouched
            // and exit.
            //
            // 🚨 The LOSER does not open the gate. The gate is ONE shared signal;
            // if the loser opened it immediately, it would open before the
            // winner's migration finished, and waiting background services
            // would query an empty schema. Measured: MT-PG-034 (the PostgreSQL
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
            _logger.LogInformation(
                "Tracon migrations are not applied automatically (AutoApplyMigrations is off). " +
                "Keeping the schema current is the caller's responsibility.");

            // The gate OPENS: the schema is the consumer's responsibility, and
            // there is no benefit in holding background services waiting forever.
            _schemaReadyGate.MarkReady();

            return;
        }

        await _runner.ApplyAsync(cancellationToken).ConfigureAwait(false);
        await EnsureDefaultTenantAsync(cancellationToken).ConfigureAwait(false);

        // 🚨 The gate opens ONLY HERE, after both the migration and the
        // default tenant write have completed. If the migration fails, the
        // gate stays closed; the host does not start anyway and waiting
        // services exit through the stoppingToken. Rationale: K-354.
        _schemaReadyGate.MarkReady();
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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

        var command = _storeContext.CreateCommand(_storeContext.Sql.UpsertTenant);
        DbHelpers.Add(command, "id", TraconId.NewId());
        DbHelpers.Add(command, "slug", tenantId);
        DbHelpers.Add(command, "display_name", tenantId);
        _storeContext.Dialect.AddTimestamp(command, "created_at", DateTimeOffset.UtcNow);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }
}
